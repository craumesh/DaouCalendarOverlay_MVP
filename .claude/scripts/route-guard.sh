#!/bin/bash
# PreToolUse guard for the Agent tool. Enforces the routing policy in CLAUDE.md.
#   lead               : needs [LEAD: plan <feature>]    -> once per feature per session
#                        or    [LEAD: revise <feature>]  -> after that plan, at most 2 times (plan failed the lint)
#                        or    [LEAD: replan <task>]     -> after >=1 senior attempt on that task, once
#   implementer        : needs [TASK: <task>]            -> recorded, used to verify escalations
#   senior-implementer : needs [ESCALATION: <condition> <task>]
#                        condition = tier-opus | failed-2x | blocked | mid-review-fix | replanned
#   mid-reviewer       : recorded (enables mid-review-fix)
#   any other agent    : allowed untouched
# State per session : .claude/logs/route-state/<session_id>.txt
# Decision log      : .claude/logs/route-guard.log
# No jq required. Blocks with a JSON deny decision plus exit 2.

input=$(cat)

field() { printf '%s' "$input" | grep -o "\"$1\" *: *\"[^\"]*\"" | head -1 | sed 's/^.*: *"//; s/"$//'; }
tag()   { printf '%s' "$input" | grep -o "\[$1: *[^]]*\]" | head -1 | sed "s/^\[$1: *//; s/ *\]\$//"; }

type=$(field subagent_type)
[ -z "$type" ] && exit 0

sid=$(field session_id); sid=${sid:-unknown}
root=${CLAUDE_PROJECT_DIR:-$PWD}
command -v cygpath >/dev/null 2>&1 && root=$(cygpath -u "$root")
logdir="$root/.claude/logs"
mkdir -p "$logdir/route-state" 2>/dev/null
state="$logdir/route-state/$sid.txt"
touch "$state" 2>/dev/null
log="$logdir/route-guard.log"
now=$(date -u +%Y-%m-%dT%H:%M:%SZ)

allow() { printf '%s %s ALLOW %s %s\n' "$now" "$sid" "$type" "$1" >> "$log" 2>/dev/null; exit 0; }
deny() {
  printf '%s %s DENY  %s %s\n' "$now" "$sid" "$type" "$2" >> "$log" 2>/dev/null
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}\n' "$1"
  printf '%s\n' "$1" >&2
  exit 2
}
count() { local c; c=$(grep -cxF "$1" "$state" 2>/dev/null); echo "${c:-0}"; }
split() {
  first=${1%% *}; rest=""
  case "$1" in *" "*) rest=${1#* } ;; esac
  rest=$(printf '%s' "$rest" | sed 's/^ *//; s/ *$//')
}

case "$type" in
  lead)
    t=$(tag LEAD); split "$t"
    case "$first" in
      plan)
        [ -n "$rest" ] || deny "lead 태그 형식 오류. 프롬프트 첫 줄을 [LEAD: plan 기능-슬러그] 로 시작할 것." "bad-plan-tag"
        [ "$(count "plan $rest")" -eq 0 ] || deny "기능 $rest 의 lead 계획은 이미 받았다. lead는 기능당 1회만 호출한다. 탐색은 Explore, 구현은 기존 계획의 spec대로 implementer에게 맡길 것." "dup-plan $rest"
        echo "plan $rest" >> "$state"; allow "plan $rest" ;;
      revise)
        [ -n "$rest" ] || deny "lead 태그 형식 오류. [LEAD: revise 기능-슬러그] 로 시작할 것." "bad-revise-tag"
        [ "$(count "plan $rest")" -ge 1 ] || deny "revise는 같은 기능의 plan 이후에만 가능하다. 먼저 [LEAD: plan $rest] 로 계획을 받을 것." "revise-no-plan $rest"
        [ "$(count "revise $rest")" -lt 2 ] || deny "기능 $rest 의 계획 수정은 이미 2회 했다. 더 진행하지 말고 사용자에게 계획 검사 오류를 보고할 것." "dup-revise $rest"
        echo "revise $rest" >> "$state"; allow "revise $rest" ;;
      replan)
        [ -n "$rest" ] || deny "lead 태그 형식 오류. [LEAD: replan 태스크ID] 로 시작할 것." "bad-replan-tag"
        [ "$(count "senior $rest")" -ge 1 ] || deny "replan은 senior-implementer가 태스크 $rest 에서 실패(2회) 또는 blocked를 보고한 뒤에만 가능하다. 지금은 구현 라우팅 규칙을 따를 것." "early-replan $rest"
        [ "$(count "replan $rest")" -eq 0 ] || deny "태스크 $rest 의 replan은 이미 1회 했다. 더 진행하지 말고 사용자에게 상황을 보고할 것." "dup-replan $rest"
        echo "replan $rest" >> "$state"; allow "replan $rest" ;;
      *)
        deny "lead는 기능당 1회 태스크 분해 전용이다. 프롬프트 첫 줄을 [LEAD: plan 기능-슬러그] 로 시작할 것. 탐색이나 질문, 방향 확인에는 lead 대신 Explore를 쓸 것." "no-tag" ;;
    esac ;;

  implementer)
    task=$(tag TASK)
    [ -n "$task" ] || deny "implementer 호출 프롬프트 첫 줄에 [TASK: 태스크ID] 태그를 붙일 것. 승격 조건 확인에 필요하다." "no-task"
    echo "impl $task" >> "$state"; allow "task $task" ;;

  senior-implementer)
    t=$(tag ESCALATION); split "$t"; reason=$first; task=$rest
    [ -n "$task" ] || deny "senior-implementer는 승격 조건에서만 부른다. 프롬프트 첫 줄을 [ESCALATION: 조건 태스크ID] 로 시작할 것. 조건은 tier-opus, failed-2x, blocked, mid-review-fix, replanned 중 하나다. 해당하지 않으면 implementer에게 맡길 것." "no-tag"
    case "$reason" in
      tier-opus)
        grep -q '^plan ' "$state" 2>/dev/null || deny "tier-opus 승격은 이 세션에 lead 계획이 있을 때만 가능하다. 계획이 없으면 implementer부터 시작할 것." "tier-opus-no-plan $task" ;;
      failed-2x)
        [ "$(count "impl $task")" -ge 2 ] || deny "failed-2x 승격은 태스크 $task 에 대한 implementer 시도가 2회 이상 있어야 한다. 먼저 implementer로 다시 시도할 것." "failed-2x-early $task" ;;
      blocked)
        [ "$(count "impl $task")" -ge 1 ] || deny "blocked 승격은 태스크 $task 에 대한 implementer 시도가 먼저 있어야 한다. implementer에게 먼저 맡길 것." "blocked-no-impl $task" ;;
      replanned)
        [ "$(count "replan $task")" -ge 1 ] || deny "replanned 승격은 태스크 $task 의 lead replan 이후에만 가능하다." "replanned-no-replan $task" ;;
      mid-review-fix)
        [ "$(count "midreview")" -ge 1 ] || deny "mid-review-fix 승격은 이 세션에 mid-reviewer 검토가 있을 때만 가능하다." "fix-no-review $task" ;;
      *)
        deny "알 수 없는 승격 조건 $reason. tier-opus, failed-2x, blocked, mid-review-fix, replanned 중 하나를 쓸 것. 해당하지 않으면 implementer에게 맡길 것." "bad-reason $reason" ;;
    esac
    echo "senior $task" >> "$state"; allow "$reason $task" ;;

  mid-reviewer)
    echo "midreview" >> "$state"; allow "" ;;

  *)
    exit 0 ;;
esac
