#!/bin/bash
# SubagentStop hook: records which agent types run and how often (for tuning routing later).
# No jq required, so it also works in Git Bash on Windows.
input=$(cat)
field() { printf '%s' "$input" | grep -o "\"$1\" *: *\"[^\"]*\"" | head -1 | sed 's/^.*: *"//; s/"$//'; }
root=${CLAUDE_PROJECT_DIR:-$PWD}
command -v cygpath >/dev/null 2>&1 && root=$(cygpath -u "$root")
mkdir -p "$root/.claude/logs" 2>/dev/null
printf '%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(field agent_type)" "$(field agent_id)" >> "$root/.claude/logs/subagents.tsv" 2>/dev/null
exit 0
