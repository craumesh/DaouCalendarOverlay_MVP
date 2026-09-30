#!/bin/bash
# SubagentStop hook for implementer / senior-implementer.
# Runs the stage-0 gate when the implementer tries to finish. If the gate fails, blocks the stop
# (exit 2) and sends the failures back so the implementer fixes them before any verifier runs.
# After GATE_MAX_BLOCKS blocks for the same agent, lets it stop and records the failure, so it can't loop forever.
# A report of status "blocked" is let through: escalation handles those.

input=$(cat)
field() { printf '%s' "$input" | grep -o "\"$1\" *: *\"[^\"]*\"" | head -1 | sed 's/^.*: *"//; s/"$//'; }

root=${CLAUDE_PROJECT_DIR:-$PWD}
command -v cygpath >/dev/null 2>&1 && root=$(cygpath -u "$root")
cwd=$(field cwd); [ -n "$cwd" ] && command -v cygpath >/dev/null 2>&1 && cwd=$(cygpath -u "$cwd")
work=${cwd:-$root}; [ -d "$work" ] || work=$root

[ -f "$root/.claude/gate.conf" ] && . "$root/.claude/gate.conf"
max=${GATE_MAX_BLOCKS:-3}

aid=$(field agent_id); aid=${aid:-unknown}
state="$root/.claude/logs/gate"; mkdir -p "$state" 2>/dev/null
count_file="$state/$aid.count"
now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
log() { printf '%s %s %s\n' "$now" "$aid" "$1" >> "$state/hook.log" 2>/dev/null; }

# Implementer reported BLOCKED: let it stop so the workflow can escalate.
if printf '%s' "$input" | grep -Eq '\\"status\\" *: *\\"blocked\\"|"status" *: *"blocked"|STATUS: *BLOCKED'; then
  log "ALLOW blocked-report"; exit 0
fi

out=$(bash "$root/.claude/scripts/gate.sh" "$work" 2>&1); code=$?
result=$(printf '%s\n' "$out" | head -1 | sed 's/^GATE RESULT: *//')

case "$code" in
  0) rm -f "$count_file"; log "ALLOW $result"; exit 0 ;;
  3) log "ALLOW partial"; exit 0 ;;   # unconfigured language: verifiers see "partial" and route to L2
  2) log "ALLOW error"; exit 0 ;;     # environment problem: not the implementer's fault; L1 reports inconclusive
esac

n=$(cat "$count_file" 2>/dev/null); n=$(( ${n:-0} + 1 ))
if [ "$n" -gt "$max" ]; then
  echo "gate failed $max times; stopping anyway" > "$state/$aid.fail"
  log "GIVE-UP after $max blocks"
  echo "0단계 게이트가 ${max}회 연속 실패해 종료를 허용한다. 보고의 status는 done으로 쓰지 말고, 게이트 실패를 notes에 그대로 적을 것." >&2
  exit 0
fi
echo "$n" > "$count_file"
log "BLOCK $n/$max"

msg="0단계 게이트 실패 (${n}/${max}). 아래 실패를 고친 뒤 다시 작업을 끝낼 것. 테스트를 약화하거나 경고를 억제해서 통과시키지 말 것.
$(printf '%s\n' "$out" | head -n 80)"
printf '%s\n' "$msg" >&2
exit 2
