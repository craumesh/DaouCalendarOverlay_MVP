// 사용법: /feature "구현할 기능 설명"
//
// Design(Fable) → Plan(Opus) → Implement(Sonnet, 실패 시 Opus 승격) + Verify(Sonnet)
//   → Mid-review(Opus, 주기적) → Final review(Fable)
//
// 모델은 각 agentType의 .claude/agents/*.md 프론트매터에서 결정된다.
// 이 스크립트는 "누가 언제 몇 번" 을 코드로 고정하는 역할만 한다.
//
// 주의: 워크플로 런타임 제약상 Date.now(), Math.random(), import 는 사용 불가.
// 스크립트를 수정하기 전에 /workflow-authoring 스킬을 로드하면 Claude가 정확한 API로 고쳐준다.

export const meta = {
  name: 'feature',
  description: 'Fable 설계 → Opus 태스크 분해 → Sonnet 구현(실패 시 Opus 승격) → 검증 → Opus 중간 검토 → Fable 최종 검토',
  phases: [
    { title: 'Design' },
    { title: 'Plan' },
    { title: 'Implement' },
    { title: 'Review' },
  ],
}

// ---- 튜닝 포인트 -----------------------------------------------------------
const MAX_SONNET_ATTEMPTS = 2   // Sonnet이 이 횟수 실패하면 Opus로 승격
const MAX_TOTAL_ATTEMPTS = 4    // 승격 후 포함 총 시도 상한 (초과 시 태스크 실패로 기록)
const MID_REVIEW_EVERY = 3      // N개 태스크마다 Opus 중간 검토 (0이면 비활성)
// ---------------------------------------------------------------------------

const goal =
  typeof args === 'string' ? args
  : args && typeof args.goal === 'string' ? args.goal
  : ''

if (!goal) {
  return { error: '목표가 비어 있습니다. 예: /feature "사용자 초대 기능 추가"' }
}

const PLAN_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['tasks'],
  properties: {
    tasks: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['id', 'title', 'spec', 'files', 'tier', 'blast_radius', 'depends_on', 'acceptance'],
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          spec: { type: 'string', description: '구현자가 추가 판단 없이 따를 수 있는 상세 지시' },
          files: { type: 'array', items: { type: 'string' } },
          tier: { type: 'string', enum: ['sonnet', 'opus'] },
          blast_radius: { type: 'string', enum: ['low', 'medium', 'high'] },
          depends_on: { type: 'array', items: { type: 'string' } },
          acceptance: { type: 'string', description: 'verifier가 실행/확인할 pass-fail 기준' },
        },
      },
    },
  },
}

const VERIFY_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['passed', 'summary', 'failures'],
  properties: {
    passed: { type: 'boolean' },
    summary: { type: 'string' },
    failures: { type: 'array', items: { type: 'string' } },
  },
}

const REVIEW_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['verdict', 'findings', 'constraints_for_next_tasks'],
  properties: {
    verdict: { type: 'string', enum: ['approve', 'changes_requested'] },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['severity', 'file', 'issue', 'fix'],
        properties: {
          severity: { type: 'string', enum: ['critical', 'major', 'minor'] },
          file: { type: 'string' },
          issue: { type: 'string' },
          fix: { type: 'string' },
        },
      },
    },
    constraints_for_next_tasks: { type: 'array', items: { type: 'string' } },
  },
}

// ---- 1. Design (Fable) ------------------------------------------------------
phase('Design')
const design = await agent(
  `다음 목표에 대한 아키텍처 설계 문서를 작성하라.\n\n목표:\n${goal}`,
  { agentType: 'architect', label: 'architect', phase: 'Design' },
)
if (!design) return { error: 'architect 단계 실패' }

// ---- 2. Plan (Opus) ---------------------------------------------------------
phase('Plan')
const plan = await agent(
  `아래 설계 문서를 구현 태스크로 분해하라. depends_on 순서대로 정렬하고, 각 태스크에 tier와 blast_radius를 판정하라.\n\n목표:\n${goal}\n\n설계 문서:\n${design}`,
  { agentType: 'lead', label: 'lead:plan', phase: 'Plan', schema: PLAN_SCHEMA },
)
if (!plan || !plan.tasks || plan.tasks.length === 0) return { error: 'lead 단계 실패', design }
log(`태스크 ${plan.tasks.length}개 (opus 시작: ${plan.tasks.filter(t => t.tier === 'opus' || t.blast_radius === 'high').length}개)`)

// ---- 3. Implement + Verify (with escalation) --------------------------------
phase('Implement')

let extraConstraints = []   // 중간 검토에서 나온 제약을 이후 태스크에 주입
const results = []

for (let i = 0; i < plan.tasks.length; i++) {
  const task = plan.tasks[i]
  const r = await implementWithEscalation(task)
  results.push(r)
  log(`${task.id} ${r.ok ? 'OK' : 'FAILED'} (${r.tier}, ${r.attempts}회)`)

  // 실패한 태스크에 의존하는 후속 태스크는 건너뛰지 않고 진행하되, 결과에 표시된다.
  // 엄격하게 막고 싶으면 여기서 break 하도록 바꾼다.

  const isLast = i === plan.tasks.length - 1
  if (MID_REVIEW_EVERY > 0 && (i + 1) % MID_REVIEW_EVERY === 0 && !isLast) {
    const mid = await agent(
      `중간 검토. 지금까지 완료된 태스크(${results.map(x => x.id).join(', ')})의 변경(git diff)이 설계 문서와 일치하는지 확인하라. 이후 태스크에 적용할 제약이 있으면 constraints_for_next_tasks에 적어라.\n\n설계 문서:\n${design}`,
      { agentType: 'mid-reviewer', label: `mid-review@${task.id}`, phase: 'Implement', schema: REVIEW_SCHEMA },
    )
    if (mid) {
      extraConstraints = extraConstraints.concat(mid.constraints_for_next_tasks || [])
      const blocking = (mid.findings || []).filter(f => f.severity !== 'minor')
      if (blocking.length > 0) {
        // 중간 검토 지적은 Opus가 바로 수정한다 (작은 누락이 큰 재작업이 되는 것을 막는 지점)
        await agent(
          `중간 검토에서 나온 지적을 수정하라. 설계 문서의 계약을 유지할 것.\n\n지적:\n${JSON.stringify(blocking, null, 2)}\n\n설계 문서:\n${design}`,
          { agentType: 'senior-implementer', label: `fix-mid-review@${task.id}`, phase: 'Implement' },
        )
      }
    }
  }
}

// ---- 4. Final review (Fable) ------------------------------------------------
phase('Review')
const review = await agent(
  `최종 검토. 전체 변경(git diff)이 설계 문서와 일치하고 불변 조건이 지켜졌는지 판정하라.\n\n설계 문서:\n${design}\n\n태스크 결과 요약:\n${JSON.stringify(results, null, 2)}`,
  { agentType: 'reviewer', label: 'final-review', phase: 'Review', schema: REVIEW_SCHEMA },
)

return {
  goal,
  verdict: review ? review.verdict : 'review_failed',
  findings: review ? review.findings : [],
  tasks: results,
  design,
}

// ---- helpers ----------------------------------------------------------------

async function implementWithEscalation(task) {
  // lead가 opus로 지정했거나 blast_radius가 high면 처음부터 Opus
  let tier = (task.tier === 'opus' || task.blast_radius === 'high') ? 'opus' : 'sonnet'
  let attempts = 0
  const history = []

  while (attempts < MAX_TOTAL_ATTEMPTS) {
    attempts++
    const agentType = tier === 'opus' ? 'senior-implementer' : 'implementer'

    const impl = await agent(buildImplPrompt(task, history), {
      agentType,
      label: `${task.id}:${tier}#${attempts}`,
      phase: 'Implement',
    })

    const check = await agent(
      `태스크 ${task.id} "${task.title}" 의 구현을 검증하라.\n\nspec:\n${task.spec}\n\n대상 파일:\n${task.files.join('\n')}\n\nacceptance:\n${task.acceptance}\n\n구현자 보고:\n${impl || '(보고 없음)'}`,
      { agentType: 'verifier', label: `${task.id}:verify#${attempts}`, phase: 'Implement', schema: VERIFY_SCHEMA },
    )

    if (check && check.passed) {
      return { id: task.id, title: task.title, ok: true, tier, attempts }
    }

    const failure = check
      ? `${check.summary}\n${(check.failures || []).join('\n')}`
      : 'verifier가 결과를 반환하지 않음'
    history.push({ attempt: attempts, tier, implReport: impl || '', failure })

    const sonnetFailures = history.filter(h => h.tier === 'sonnet').length
    if (tier === 'sonnet' && sonnetFailures >= MAX_SONNET_ATTEMPTS) {
      tier = 'opus'
      log(`${task.id}: Sonnet ${sonnetFailures}회 실패 → Opus로 승격`)
    }
  }

  return { id: task.id, title: task.title, ok: false, tier, attempts, lastFailure: history[history.length - 1].failure }
}

function buildImplPrompt(task, history) {
  let p = `태스크 ${task.id}: ${task.title}\n\nspec:\n${task.spec}\n\n대상 파일:\n${task.files.join('\n')}\n\nacceptance (완료 판정 기준):\n${task.acceptance}\n\n설계 문서:\n${design}`
  if (extraConstraints.length > 0) {
    p += `\n\n중간 검토에서 추가된 제약:\n- ${extraConstraints.join('\n- ')}`
  }
  if (history.length > 0) {
    p += `\n\n이전 시도 실패 이력 (근본 원인부터 진단할 것):\n${history.map(h => `[시도 ${h.attempt}, ${h.tier}]\n구현자 보고: ${h.implReport}\n검증 실패: ${h.failure}`).join('\n\n')}`
  }
  return p
}
