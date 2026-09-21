#!/bin/bash
# SubagentStop 훅: 어떤 에이전트 타입이 얼마나 자주 호출되는지 기록한다.
# 나중에 라우팅(어떤 작업이 Opus로 새는지, Fable이 과호출되는지)을 튜닝할 때 본다.
INPUT=$(cat)
AGENT_TYPE=$(echo "$INPUT" | jq -r '.agent_type // "unknown"' 2>/dev/null)
AGENT_ID=$(echo "$INPUT" | jq -r '.agent_id // ""' 2>/dev/null)
mkdir -p .claude/logs
printf '%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$AGENT_TYPE" "$AGENT_ID" >> .claude/logs/subagents.tsv
exit 0
