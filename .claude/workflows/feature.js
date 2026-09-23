// 사용법: /feature "구현할 기능 설명"
//
// Explore(Sonnet) → Design(Fable) → Plan(Opus, 1회) → Implement(Sonnet, 승격 조건일 때만 Opus) + Verify(Sonnet)
//   → Mid-review(Opus, 주기적) → Final review(Fable)
//
// 탐색은 Explore가 한 번만 하고, 그 결과를 architect·lead·구현자에게 넘겨 같은 파일을 다시 뒤지지 않게 한다.
// lead·implementer·senior-implementer 프롬프트 첫 줄에는 CLAUDE.md와 같은 라우팅 태그를 붙인다
// ([LEAD: plan ...], [TASK: ...], [ESCALATION: <조건> ...]). 로그에서 왜 그 모델이 쓰였는지 추적할 수 있다.
//
// ── 실패 처리 원칙 ─────────────────────────────────────────────────────────
// 실패를 두 종류로 나누고 절대 섞지 않는다.
//
// 1) 인프라 실패: 모델 능력과 무관하게 "그 단계가 제대로 실행되지 못한" 경우
//    - agent()가 null로 끝남 (런타임 내부 재시도로도 복구 못 한 529/5xx 등 API 오류,
//      /workflows에서 개별 agent 중지, auto 모드 분류기 차단)
//    - agent()가 예외를 던짐 (구조화 출력 검증 반복 실패 등)
//    - 결과가 잘림 (설계 문서 완결 마커 없음)
//    - verifier가 inconclusive 판정 (네트워크·테스트 인프라 문제로 검증 자체를 못 함)
//    → 같은 단계를 INFRA_RETRIES회 재시도한다. 에스컬레이션 카운트에 넣지 않는다.
//    → 재시도가 소진되면 run 전체를 그 자리에서 멈추고 status: 'halted'를 반환한다.
//      절대 건너뛰고 다음 단계로 가지 않는다.
//
// 2) 작업 실패: 단계는 실행됐지만 결과가 기준에 못 미친 경우
//    - verifier가 fail 판정, 또는 구현자가 blocked 보고
//    → Sonnet이 MAX_SONNET_ATTEMPTS회 실패(또는 blocked)하면 Opus로 승격한다.
//    → 끝내 실패하면 그 태스크는 failed, 그 태스크에 의존하는 태스크는 skipped.
//    → 하나라도 있으면 최종 status는 'incomplete'이고 Fable 최종 검토는 돌리지 않는다.
//
// ── halted 이후 재개 ───────────────────────────────────────────────────────
// /feature를 새로 실행하지 말 것 (설계부터 다시 돈다).
// 같은 세션에서 Claude에게 "멈춘 feature 워크플로를 같은 스크립트로 relaunch 해줘"라고 요청하면
// 완료된 agent는 저장된 결과를 재사용하고 멈춘 지점부터 다시 실행된다.
//
// 런타임 제약: Date.now(), Math.random(), import 사용 불가.
// 수정 전에는 /workflow-authoring 스킬을 로드하고 Claude에게 맡기는 것이 안전하다.

export const meta = {
  name: 'feature',
  description: 'Sonnet 탐색 → Fable 설계 → Opus 태스크 분해(1회) → Sonnet 구현(승격 조건일 때만 Opus) → 검증 → Opus 중간 검토 → Fable 최종 검토',
  phases: [
    { title: 'Design' },
    { title: 'Plan' },
    { title: 'Implement' },
    { title: 'Review' },
  ],
}

// ---- 튜닝 포인트 -----------------------------------------------------------
const MAX_SONNET_ATTEMPTS = 2   // Sonnet 작업 실패가 이 횟수에 도달하면 Opus로 승격
const MAX_TOTAL_ATTEMPTS = 4    // 승격 포함 작업 실패 총 상한 (도달 시 태스크 failed)
const MID_REVIEW_EVERY = 3      // 완료 태스크 N개마다 Opus 중간 검토 (0이면 비활성)
const INFRA_RETRIES = 2         // 인프라 실패 시 같은 단계 재시도 횟수 (소진 시 run 중단)
// ---------------------------------------------------------------------------

const DESIGN_END_MARKER = 'END-OF-DESIGN'

const RETRY_NOTE =
  '\n\n(참고: 이 단계의 직전 실행이 결과를 내지 못하고 끝났다 — 서버 오류, 중단, 또는 검증 환경 문제. ' +
  '파일을 수정하는 작업이라면 직전 실행이 남긴 부분 변경이 있을 수 있으니, ' +
  '먼저 git status와 git diff로 현재 상태를 확인한 뒤 이어서 완료하라.)'

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
        required: ['id', 'title', 'spec', 'files', 'tier', 'tier_reason', 'blast_radius', 'depends_on', 'acceptance'],
        properties: {
          id: { type: 'string' },
          title: { type: 'string' },
          spec: { type: 'string', description: '구현자가 추가 판단 없이 따를 수 있는 상세 지시' },
          files: { type: 'array', items: { type: 'string' } },
          tier: { type: 'string', enum: ['sonnet', 'opus'] },
          tier_reason: { type: 'string', description: 'opus면 해당하는 판정 기준, sonnet이면 "default"' },
          blast_radius: { type: 'string', enum: ['low', 'medium', 'high'] },
          depends_on: { type: 'array', items: { type: 'string' } },
          acceptance: { type: 'string', description: 'verifier가 실행/확인할 pass-fail 기준' },
        },
      },
    },
  },
}

// 구현자 보고를 구조화한다. 잘린 출력은 스키마를 통과하지 못하므로 성공으로 둔갑할 수 없다.
const IMPL_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['status', 'changed_files', 'verification', 'unverified', 'notes'],
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    changed_files: { type: 'array', items: { type: 'string' } },
    verification: { type: 'string', description: '직접 실행한 검증 명령과 결과 요약' },
    unverified: { type: 'string', description: '실행하지 못했거나 확인하지 못한 항목. 없으면 "없음"' },
    notes: { type: 'string', description: 'spec과 다르게 한 부분, blocked 사유, 근본 원인, 다룬 엣지 케이스, 설계 이탈 등' },
  },
}

const VERIFY_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['result', 'summary', 'failures'],
  properties: {
    result: {
      type: 'string',
      enum: ['pass', 'fail', 'inconclusive'],
      description: 'inconclusive는 코드와 무관한 이유(네트워크, 외부 서비스 오류, 테스트 인프라 미기동 등)로 검증을 끝내지 못했을 때만',
    },
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

const goal =
  typeof args === 'string' ? args
  : args && typeof args.goal === 'string' ? args.goal
  : ''

if (!goal) {
  return { status: 'invalid_input', error: '목표가 비어 있습니다. 예: /feature "사용자 초대 기능 추가"' }
}

// lead plan 태그용 기능 슬러그. 목표 문자열에서 결정적으로 만든다.
const FEATURE_SLUG = slugOf(goal)

// 진행 상태. halt 시 어디까지 됐는지 보고하는 데 쓴다.
const state = {
  stage: 'init',
  recon: '',        // Explore 탐색 결과
  design: '',
  tasks: [],
  results: [],
  currentTask: null,
  constraints: [],   // 중간 검토가 이후 태스크에 주입하는 제약
  unresolved: [],    // 수정되지 못한 중간 검토 지적
  infraEvents: [],   // 인프라 실패 기록 (몇 번, 어디서 529 등을 맞았는지)
}

try {
  return await run()
} catch (e) {
  if (!e || !e.halt) throw e   // 스크립트 버그는 그대로 터뜨려서 드러낸다
  const finished = new Set(state.results.map(r => r.id))
  return {
    status: 'halted',
    halted_at: e.step,
    reason: e.message,
    stage: state.stage,
    in_progress_task: state.currentTask,
    finished_tasks: state.results,
    not_started: state.tasks
      .filter(t => !finished.has(t.id) && t.id !== state.currentTask)
      .map(t => t.id),
    infra_events: state.infraEvents,
    next:
      '/feature를 새로 실행하지 말 것(설계부터 다시 돈다). 원인이 서버 오류면 잠시 뒤 같은 세션에서 ' +
      '이 run을 같은 스크립트로 relaunch하면, 완료된 agent는 저장된 결과를 재사용하고 멈춘 지점부터 재개된다.',
  }
}

// ============================================================================

async function run() {
  // ---- 0. Explore (Sonnet) --------------------------------------------------
  phase('Design')
  state.stage = 'explore'
  state.recon = await step(
    `다음 목표와 관련된 코드베이스 현황을 조사하라. 설계자와 태스크 분해자가 파일을 다시 뒤지지 않아도 되도록 관련 파일, 핵심 시그니처, 기존 관례, 테스트 위치를 정리하라. 코드는 수정하지 말 것.\n\n목표:\n${goal}`,
    { agentType: 'Explore', label: 'explore', phase: 'Design' },
    r => (typeof r === 'string' && r.trim().length > 0) || '탐색 결과가 비어 있음',
  )

  // ---- 1. Design (Fable) ----------------------------------------------------
  state.stage = 'design'
  state.design = await step(
    `다음 목표에 대한 아키텍처 설계 문서를 작성하라. 아래 탐색 결과로 충분한 부분은 파일을 다시 읽지 말고, 설계 결정에 꼭 필요한 파일만 추가로 확인하라.\n\n목표:\n${goal}\n\n탐색 결과(Explore):\n${state.recon}`,
    { agentType: 'architect', label: 'architect', phase: 'Design' },
    d => (typeof d === 'string' && d.includes(DESIGN_END_MARKER)) || '설계 문서에 완결 마커가 없음 (출력이 잘렸을 가능성)',
  )

  // ---- 2. Plan (Opus) -------------------------------------------------------
  phase('Plan')
  state.stage = 'plan'
  const plan = await step(
    `[LEAD: plan ${FEATURE_SLUG}]\n아래 설계 문서를 구현 태스크로 분해하라. depends_on 순서대로 정렬하고, 각 태스크에 tier와 blast_radius를 판정하라. tier는 기본 sonnet이고, opus면 판정 기준을 tier_reason에 적어라. 탐색 결과로 충분한 부분은 파일을 다시 읽지 말 것.\n\n목표:\n${goal}\n\n탐색 결과(Explore):\n${state.recon}\n\n설계 문서:\n${state.design}`,
    { agentType: 'lead', label: 'lead:plan', phase: 'Plan', schema: PLAN_SCHEMA },
    p => (Array.isArray(p.tasks) && p.tasks.length > 0) || '태스크 목록이 비어 있음',
  )
  state.tasks = plan.tasks
  log(`태스크 ${plan.tasks.length}개 (처음부터 Opus: ${plan.tasks.filter(startsOnOpus).length}개)`)

  // ---- 3. Implement + Verify ------------------------------------------------
  phase('Implement')
  state.stage = 'implement'
  const statusById = {}
  let doneSinceReview = []

  for (let i = 0; i < plan.tasks.length; i++) {
    const task = plan.tasks[i]
    state.currentTask = task.id

    // 선행 태스크가 done이 아니면 실행하지 않는다 (깨진 기반 위에 쌓지 않는다)
    const blockedBy = (task.depends_on || []).filter(d => statusById[d] !== 'done')
    const r = blockedBy.length > 0
      ? { id: task.id, title: task.title, status: 'skipped', reason: `선행 태스크 미완료: ${blockedBy.join(', ')}` }
      : await implementWithEscalation(task)

    statusById[task.id] = r.status
    state.results.push(r)
    state.currentTask = null
    log(`${task.id} ${r.status}${r.status === 'skipped' ? ` — ${r.reason}` : ` (${r.tier}, 작업 시도 ${r.attempts}회)`}`)

    if (r.status === 'done') doneSinceReview.push(task)
    const isLast = i === plan.tasks.length - 1
    if (MID_REVIEW_EVERY > 0 && doneSinceReview.length >= MID_REVIEW_EVERY && !isLast) {
      await midReview(doneSinceReview)
      doneSinceReview = []
    }
  }

  // ---- 4. Final review (Fable) ----------------------------------------------
  const notDone = state.results.filter(r => r.status !== 'done')
  if (notDone.length > 0 || state.unresolved.length > 0) {
    // 불완전한 결과에는 Fable 최종 검토를 쓰지 않는다. 사람이 먼저 판단할 일이다.
    return {
      status: 'incomplete',
      goal,
      final_review: 'not_run',
      not_done: notDone,
      unresolved_mid_review: state.unresolved,
      tasks: state.results,
      infra_events: state.infraEvents,
    }
  }

  phase('Review')
  state.stage = 'final-review'
  const review = await step(
    `최종 검토. 전체 변경(git diff)이 설계 문서와 일치하고 불변 조건이 지켜졌는지 판정하라.\n\n설계 문서:\n${state.design}\n\n태스크 결과 요약:\n${JSON.stringify(state.results, null, 2)}`,
    { agentType: 'reviewer', label: 'final-review', phase: 'Review', schema: REVIEW_SCHEMA },
  )

  return {
    status: review.verdict === 'approve' ? 'complete' : 'changes_requested',
    goal,
    verdict: review.verdict,
    findings: review.findings,
    tasks: state.results,
    infra_events: state.infraEvents,
    design: state.design,
  }
}

// ---- 공통: 모든 agent 호출은 step()을 거친다 ---------------------------------
// 반환값은 항상 유효한 결과이고 절대 null이 아니다. 그래서 호출부에는 null 분기가 없고,
// "죽은 단계를 조용히 건너뛰는" 코드가 들어갈 자리 자체가 없다.
async function step(prompt, opts, validate) {
  let reason = ''
  for (let i = 0; i <= INFRA_RETRIES; i++) {
    const label = i === 0 ? opts.label : `${opts.label}~retry${i}`
    reason = ''
    let out = null
    try {
      out = await agent(i === 0 ? prompt : prompt + RETRY_NOTE, { ...opts, label })
    } catch (e) {
      reason = `예외: ${e && e.message ? e.message : String(e)}`
    }
    if (!reason && (out === null || out === undefined)) {
      reason = '결과 없음 (서버 오류로 종료, 개별 중지, 또는 auto 모드 차단)'
    }
    if (!reason && validate) {
      const ok = validate(out)
      if (ok !== true) reason = String(ok)
    }
    if (!reason) return out

    state.infraEvents.push({ step: label, reason })
    log(`[인프라] ${label}: ${reason}${i < INFRA_RETRIES ? ' → 재시도' : ' → 재시도 소진, run 중단'}`)
  }
  const err = new Error(`${opts.label}: 인프라 실패 ${INFRA_RETRIES + 1}회 연속 — ${reason}`)
  err.halt = true
  err.step = opts.label
  throw err
}

// ---- 구현 + 검증 + 승격 ------------------------------------------------------
async function implementWithEscalation(task) {
  let tier = startsOnOpus(task) ? 'opus' : 'sonnet'
  let reason = tier === 'opus' ? 'tier-opus' : null   // senior-implementer로 가는 승격 조건
  const history = []   // 작업 실패만 쌓인다. 인프라 실패는 step()이 흡수하므로 여기 들어오지 않는다.

  while (history.length < MAX_TOTAL_ATTEMPTS) {
    const n = history.length + 1
    const tagLine = tier === 'opus' ? `[ESCALATION: ${reason} ${task.id}]` : `[TASK: ${task.id}]`
    const impl = await step(buildImplPrompt(task, history, tagLine), {
      agentType: tier === 'opus' ? 'senior-implementer' : 'implementer',
      label: `${task.id}:${tier}#${n}`,
      phase: 'Implement',
      schema: IMPL_SCHEMA,
    })

    let failure
    if (impl.status === 'blocked') {
      failure = `구현자 blocked 보고: ${impl.notes}`
    } else {
      const check = await step(buildVerifyPrompt(task, impl), {
        agentType: 'verifier',
        label: `${task.id}:verify#${n}`,
        phase: 'Implement',
        schema: VERIFY_SCHEMA,
      }, conclusive)
      if (check.result === 'pass') {
        return { id: task.id, title: task.title, status: 'done', tier, attempts: n, ...(reason ? { escalation: reason } : {}) }
      }
      failure = describe(check)
    }
    history.push({ attempt: n, tier, impl, failure })

    if (tier === 'opus' && impl.status === 'blocked') {
      // Opus의 blocked는 대개 설계 계약을 깨야만 가능한 경우다. 같은 spec으로 다시 돌려도 소용없다.
      break
    }
    if (tier === 'sonnet') {
      const sonnetFailures = history.filter(h => h.tier === 'sonnet').length
      if (impl.status === 'blocked' || sonnetFailures >= MAX_SONNET_ATTEMPTS) {
        tier = 'opus'
        reason = impl.status === 'blocked' ? 'blocked' : 'failed-2x'
        log(`${task.id}: Sonnet 작업 실패 ${sonnetFailures}회${impl.status === 'blocked' ? '(blocked)' : ''} → Opus로 승격`)
      }
    }
  }

  return {
    id: task.id,
    title: task.title,
    status: 'failed',
    tier,
    ...(reason ? { escalation: reason } : {}),
    attempts: history.length,
    last_failure: history[history.length - 1].failure,
  }
}

// ---- 중간 검토 (Opus) ---------------------------------------------------------
async function midReview(batch) {
  const at = batch[batch.length - 1].id
  state.stage = `mid-review@${at}`

  const mid = await step(
    `중간 검토. 최근 완료된 태스크(${batch.map(t => t.id).join(', ')})의 변경(git diff)이 설계 문서와 일치하는지 확인하라. 이후 태스크에 적용할 제약이 있으면 constraints_for_next_tasks에 적어라.\n\n설계 문서:\n${state.design}`,
    { agentType: 'mid-reviewer', label: `mid-review@${at}`, phase: 'Implement', schema: REVIEW_SCHEMA },
  )
  state.constraints.push(...(mid.constraints_for_next_tasks || []))

  const blocking = (mid.findings || []).filter(f => f.severity !== 'minor')
  if (blocking.length > 0) {
    // 지적은 Opus가 바로 고치고, 고친 결과도 verifier로 확인한다
    const fix = await step(
      `[ESCALATION: mid-review-fix ${at}]\n중간 검토에서 나온 지적을 수정하라. 설계 문서의 인터페이스 계약과 불변 조건을 유지할 것.\n\n지적:\n${JSON.stringify(blocking, null, 2)}\n\n설계 문서:\n${state.design}`,
      { agentType: 'senior-implementer', label: `fix@${at}`, phase: 'Implement', schema: IMPL_SCHEMA },
    )

    let unresolvedReason = null
    if (fix.status === 'blocked') {
      unresolvedReason = `수정자 blocked 보고: ${fix.notes}`
    } else {
      const check = await step(
        `중간 검토 지적이 해소됐는지, 그리고 아래 태스크들의 acceptance가 여전히 통과하는지 검증하라.\n\n지적:\n${JSON.stringify(blocking, null, 2)}\n\n태스크 acceptance:\n${batch.map(t => `[${t.id}] ${t.acceptance}`).join('\n')}\n\n수정자 보고:\n${JSON.stringify(fix, null, 2)}`,
        { agentType: 'verifier', label: `verify-fix@${at}`, phase: 'Implement', schema: VERIFY_SCHEMA },
        conclusive,
      )
      if (check.result !== 'pass') unresolvedReason = describe(check)
    }

    if (unresolvedReason) {
      state.unresolved.push({ at, findings: blocking, reason: unresolvedReason })
      log(`[미해결] mid-review@${at} 지적이 해소되지 않음 → 최종 status는 complete가 될 수 없음`)
    }
  }
  state.stage = 'implement'
}

// ---- helpers ----------------------------------------------------------------
function slugOf(s) {
  const ascii = s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 30)
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0
  return (ascii || 'feature') + '-' + h.toString(16)
}

function startsOnOpus(task) {
  return task.tier === 'opus' || task.blast_radius === 'high'
}

// inconclusive는 "코드가 틀렸다"가 아니라 "확인을 못 했다"이므로 인프라 실패로 취급한다
function conclusive(v) {
  return v.result !== 'inconclusive' || `검증을 끝내지 못함(inconclusive): ${v.summary}`
}

function describe(check) {
  return [check.summary].concat(check.failures || []).join('\n')
}

function buildImplPrompt(task, history, tagLine) {
  let p =
    `${tagLine}\n태스크 ${task.id}: ${task.title}\n\nspec:\n${task.spec}\n\n대상 파일:\n${task.files.join('\n')}` +
    `\n\nacceptance (완료 판정 기준):\n${task.acceptance}\n\n설계 문서:\n${state.design}` +
    `\n\n탐색 결과(Explore, 파일을 다시 뒤지기 전에 먼저 참고):\n${state.recon}`
  if (state.constraints.length > 0) {
    p += `\n\n중간 검토에서 추가된 제약:\n- ${state.constraints.join('\n- ')}`
  }
  if (history.length > 0) {
    p += '\n\n이전 시도 실패 이력 (근본 원인부터 진단할 것):\n' + history
      .map(h => `[시도 ${h.attempt}, ${h.tier}]\n구현자 보고: ${JSON.stringify(h.impl)}\n실패 내용: ${h.failure}`)
      .join('\n\n')
  }
  return p
}

function buildVerifyPrompt(task, impl) {
  return (
    `태스크 ${task.id} "${task.title}" 의 구현을 검증하라.\n\nspec:\n${task.spec}` +
    `\n\n대상 파일:\n${task.files.join('\n')}\n\nacceptance:\n${task.acceptance}` +
    `\n\n구현자 보고:\n${JSON.stringify(impl, null, 2)}`
  )
}
