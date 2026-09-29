#!/bin/bash
# Stage-0 gate: build / type-check / lint / test with tools only, no model.
# Picks the commands to run from the languages of the changed files, using .claude/gate.conf.
#
# Usage: bash .claude/scripts/gate.sh [project-dir]
# First output line: "GATE RESULT: <pass|fail|error|partial|skip>"
#   pass    every gate that applied ran and passed
#   fail    at least one gate failed                         -> the code is wrong
#   error   a gate could not run (command missing, exit 126/127) -> environment problem
#   partial changed files include a language with no command in gate.conf; the rest passed
#   skip    nothing changed that any gate covers
# Exit code: 0 pass/skip, 1 fail, 2 error, 3 partial.
# Also writes the full report to .claude/logs/gate/last.txt

dir=${1:-${CLAUDE_PROJECT_DIR:-$PWD}}
command -v cygpath >/dev/null 2>&1 && dir=$(cygpath -u "$dir")
cd "$dir" 2>/dev/null || { echo "GATE RESULT: error"; echo "cannot cd to $dir"; exit 2; }

conf=".claude/gate.conf"
[ -f "$conf" ] || conf="${CLAUDE_PROJECT_DIR:-$dir}/.claude/gate.conf"
# shellcheck disable=SC1090
[ -f "$conf" ] && . "$conf"
tail_lines=${GATE_OUTPUT_TAIL:-60}

# ---- which files changed ----------------------------------------------------
changed=""
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  changed=$( { git diff --name-only HEAD 2>/dev/null || git diff --name-only; git ls-files --others --exclude-standard; } | grep -v '^\.claude/logs/' | sort -u)
  in_git=1
else
  in_git=0
fi

has() { printf '%s\n' "$changed" | grep -Eiq "$1"; }

langs=""
add() { case " $langs " in *" $1 "*) ;; *) langs="$langs $1" ;; esac; }
if [ "$in_git" -eq 0 ] || [ "${GATE_RUN_ALL:-0}" = "1" ]; then
  # Not a git repo (or forced): run every configured gate.
  for l in CPP CSHARP UNITY JS RUST PY; do add $l; done
else
  has '\.(c|h|cc|cpp|cxx|hh|hpp|hxx|inl|ipp)$|(^|/)CMakeLists\.txt$|\.cmake$' && add CPP
  if has '\.(cs|asmdef|asmref)$|(^|/)Packages/manifest\.json$'; then
    if [ -f ProjectSettings/ProjectVersion.txt ] || [ -n "${GATE_UNITY_FORCE:-}" ]; then add UNITY; else add CSHARP; fi
  fi
  has '\.(js|jsx|mjs|cjs|ts|tsx|mts|cts)$|(^|/)package\.json$|(^|/)tsconfig[^/]*\.json$' && add JS
  has '\.rs$|(^|/)Cargo\.(toml|lock)$' && add RUST
  has '\.(py|pyi)$|(^|/)pyproject\.toml$' && add PY
fi

cmd_for() {
  case "$1" in
    CPP) echo "${GATE_CPP:-}" ;; CSHARP) echo "${GATE_CSHARP:-}" ;; UNITY) echo "${GATE_UNITY:-}" ;;
    JS) echo "${GATE_JS_TS:-}" ;; RUST) echo "${GATE_RUST:-}" ;; PY) echo "${GATE_PYTHON:-}" ;;
  esac
}

report=$(mktemp 2>/dev/null || echo "/tmp/gate.$$")
summary=""
fails=0; errors=0; missing=0; ran=0

run() { # name command
  local name=$1 cmd=$2 out code
  ran=$((ran+1))
  out=$(bash -c "$cmd" 2>&1); code=$?
  if [ $code -eq 0 ]; then
    summary="$summary\n[$name] pass : $cmd"
  elif [ $code -eq 126 ] || [ $code -eq 127 ]; then
    errors=$((errors+1)); summary="$summary\n[$name] ERROR (exit $code, command not runnable) : $cmd"
    printf '\n----- %s (exit %s) -----\n%s\n' "$name" "$code" "$(printf '%s\n' "$out" | tail -n "$tail_lines")" >> "$report"
  else
    fails=$((fails+1)); summary="$summary\n[$name] FAIL (exit $code) : $cmd"
    printf '\n----- %s (exit %s) -----\n%s\n' "$name" "$code" "$(printf '%s\n' "$out" | tail -n "$tail_lines")" >> "$report"
  fi
}

for l in $langs; do
  c=$(cmd_for "$l")
  if [ -z "$c" ]; then
    if [ "$in_git" -eq 1 ] && [ "${GATE_RUN_ALL:-0}" != "1" ]; then
      missing=$((missing+1)); summary="$summary\n[$l] NOT CONFIGURED : changed files need a GATE_ command in .claude/gate.conf"
    fi
    continue
  fi
  run "$l" "$c"
done

# Extra gates for risky changes
if [ -n "${GATE_CPP_SANITIZE:-}" ] && printf ' %s ' "$langs" | grep -q ' CPP '; then run CPP-SANITIZE "$GATE_CPP_SANITIZE"; fi
if [ -n "${GATE_RUST_MIRI:-}" ] && printf ' %s ' "$langs" | grep -q ' RUST ' && git diff HEAD -- '*.rs' 2>/dev/null | grep -q '^+.*\bunsafe\b'; then run RUST-MIRI "$GATE_RUST_MIRI"; fi
[ -n "${GATE_ALWAYS:-}" ] && [ -n "$changed$langs" ] && run ALWAYS "$GATE_ALWAYS"

if [ $fails -gt 0 ]; then result=fail; code=1
elif [ $errors -gt 0 ]; then result=error; code=2
elif [ $missing -gt 0 ]; then result=partial; code=3
elif [ $ran -eq 0 ]; then result=skip; code=0
else result=pass; code=0; fi

files_n=$(printf '%s\n' "$changed" | grep -c . )
{
  echo "GATE RESULT: $result"
  echo "changed files: $files_n  languages:${langs:- none}"
  printf '%b\n' "$summary" | sed '/^$/d'
  cat "$report" 2>/dev/null
} | tee /dev/null > "$report.final"
mkdir -p .claude/logs/gate 2>/dev/null && cp "$report.final" .claude/logs/gate/last.txt 2>/dev/null
cat "$report.final"
rm -f "$report" "$report.final"
exit $code
