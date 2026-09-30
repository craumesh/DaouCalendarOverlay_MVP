// 사용법: /feature "구현할 기능 설명"
//
// 모델은 Opus 5.5(claude-opus-5-5)와 Sonnet 5.5(claude-sonnet-5-5) 두 개만 쓴다. 모든 agent() 호출에 버전까지 지정한다.
//
// Explore(Sonnet) → Design(Opus) → Plan(Opus, 인수 조건 포함) → 계획 검사(모델 없음)
//   → 태스크별 구현(Sonnet 기본, 조건부 Opus) → 검증 0단계(도구) → 1단계(Sonnet) → 2단계(Opus, 조건부)
//   → 완료 3개마다 중간 검토(Opus) → 최종 검토(Opus, 새 컨텍스트)
//
// ── 검증 라우팅 ───────────────────────────────────────────────────────────
// 0단계: 구현자가 끝내려 할 때 SubagentStop 훅이 gate.sh를 돌려 실패하면 되돌려 보낸다(도구만).
// 1단계: verifier-l1(Sonnet)이 게이트를 다시 돌리고 인수 조건을 체크리스트로 판정한다.
// 2단계: verifier-l2(Opus)가 새 컨텍스트에서 적대적으로 검증하고 최종 승인한다.
// 1단계로 끝나는 것은 "저위험"뿐이다: Sonnet 구현 + blast_radius low + 위험 태그 없음 + 위험 경로 미변경
//   + decided_by 2 조건 없음 + 1단계 전부 확신 있는 pass + 게이트가 실제로 돌아 통과.
// 검증자는 구현자의 자가 보고(설명·검증 주장)를 받지 않는다. 바뀐 파일 목록만 받는다.
//
// ── 구현 승격 ─────────────────────────────────────────────────────────────
// Sonnet#1 → 검증 → Sonnet#2(검증자 소견 포함) → Opus#1 → Opus#2 → lead replan → Opus#3 → 실패
// Sonnet이 blocked를 보고하면 바로 Opus로, Opus가 blocked면 바로 replan으로 간다.
//
// ── 구현 비율 ─────────────────────────────────────────────────────────────
// 목표 Opus:Sonnet = 3:7, 허용 최대 4:6 (구현 토큰 기준). 실행 중에는 토큰을 볼 수 없으므로
// 태스크 크기 가중치(S=1, M=2, L=4)로 추정한다. 계획 단계에서 처음부터 Opus로 배정하는 몫이 40%를 넘으면
// 계획을 되돌린다. 실행 중 추정치가 40%를 넘으면 멈추지 않고 결과에 표시한다(정확성 우선).
// 실제 토큰 비율은 끝난 뒤 usage-by-model.ps1 의 [4]로 확인한다.
//
// ── 실패 처리 ─────────────────────────────────────────────────────────────
// 인프라 실패(agent()가 null·예외, 잘린 출력, 검증 불가)는 같은 단계를 재시도하고, 소진되면 run 전체를 멈춘다.
// 건너뛰고 다음 단계로 가지 않는다. 작업 실패(검증 fail, blocked)만 승격으로 이어진다.
// halted면 /feature를 새로 실행하지 말고 같은 세션에서 같은 스크립트로 relaunch한다.
//
// 런타임 제약: Date.now(), Math.random(), import 사용 불가. 수정 전 /workflow-authoring 스킬을 로드할 것.

export const meta = {
  name: 'feature',
  description: 'Opus 5.5/Sonnet 5.5 전용: 탐색 → 설계 → 인수 조건 계획 → Sonnet 우선 구현(조건부 Opus) → 0/1/2단계 검증 → 중간·최종 검토',
  phases: [
    { title: 'Design' },
    { title: 'Plan' },
    { title: 'Implement' },
    { title: 'Review' },
  ],
}

// ---- 모델 (버전 고정) -------------------------------------------------------
const OPUS = 'claude-opus-5-5'
const SONNET = 'claude-sonnet-5-5'
const MODEL_FOR = {
  Explore: SONNET,
  architect: OPUS,
  lead: OPUS,
  implementer: SONNET,
  'senior-implementer': OPUS,
  'verifier-l1': SONNET,
  'verifier-l2': OPUS,
  'mid-reviewer': OPUS,
  'final-reviewer': OPUS,
}

// ---- 튜닝 포인트 -----------------------------------------------------------
const MAX_SONNET_ATTEMPTS = 2   // Sonnet 작업 실패가 이 횟수에 도달하면 Opus로 승격
const MAX_OPUS_ATTEMPTS = 2     // Opus 작업 실패가 이 횟수에 도달하면 lead replan
const MID_REVIEW_EVERY = 3      // 완료 태스크 N개마다 중간 검토 (0이면 비활성)
const INFRA_RETRIES = 2         // 인프라 실패 시 같은 단계 재시도 횟수 (소진 시 run 중단)
const PLAN_REVISIONS = 2        // 계획 검사 실패 시 lead에게 되돌리는 최대 횟수
const SIZE_WEIGHT = { S: 1, M: 2, L: 4 }
const PLAN_OPUS_TARGET = 0.2    // 계획 단계 Opus 배정 목표 (넘으면 경고)
const PLAN_OPUS_MAX = 0.4       // 계획 단계 Opus 배정 상한 (넘으면 계획 반려)
const RATIO_TARGET = 0.3        // 구현 Opus 비율 목표
const RATIO_MAX = 0.4           // 구현 Opus 비율 허용 최대 (넘으면 결과에 표시)

// 변경되면 저위험으로 보지 않는 경로. 태그를 빠뜨려도 실제로 바뀐 파일로 걸러낸다. 프로젝트 구조에 맞게 고칠 것.
// DaouCalendarOverlay(.NET 8 WPF + Chrome MV3 확장 + Native Messaging/Named Pipe)에 맞춘 목록이다.
const RISK_PATHS = [
  // 쿠키·세션·권한, 파이프 신원 확인, BaseUrl 검증, 확장 manifest(권한·host_permissions·key)
  { tag: 'security', re: /(auth|crypt|cipher|secur|permission|credential|secret|token|session|cookie|PipePeerVerifier|NativeHostRelay|BaseUrlPolicy|ChromeExtension\/manifest\.json$)/i },
  // 캐시·설정 파일 포맷과 원자적 저장, 사용자 데이터·레지스트리 삭제
  { tag: 'migration', re: /(migrat|schema|serializ|persist|CacheService|CacheRangePolicy|SettingsService|AtomicJsonFileWriter|AppSettings\.cs$|Uninstall|NativeMessagingRegistration|StartupService)/i },
  // 확장↔host↔앱 브리지 프로토콜(메시지 스키마, 파이프 이름, protocolVersion)과 확장 worker
  { tag: 'network-sync', re: /(protocol|Bridge|NativeMessaging|service-worker|ExtensionVersionGuard|NoFetchReasons)/i },
  // 파이프 서버 스레드, 싱글 인스턴스 Mutex, 로그 파일 경합, 종료 경로
  { tag: 'concurrency', re: /(thread|mutex|atomic|concurren|parallel|SingleInstance|LogService|CalendarBridgeServer)/i },
  // 프로세스 진입점(GUI/native host/제거 모드 분기)과 공개 계약
  { tag: 'public-api', re: /((^|\/)Program\.cs$|StartupModeParser|App\.xaml\.cs$)/i },
]

const DESIGN_END_MARKER = 'END-OF-DESIGN'

// ---- 스키마 ------------------------------------------------------------------
const CRITERION = {
  type: 'object',
  additionalProperties: false,
  required: ['id', 'kind', 'check', 'expected', 'decided_by', 'new_test'],
  properties: {
    id: { type: 'string', description: '"<태스크id>-A<번호>", 예: T3-A1' },
    kind: { type: 'string', enum: ['test', 'command', 'benchmark', 'property', 'inspection'] },
    check: { type: 'string', description: '정확한 명령이나 테스트 이름. inspection이면 볼 파일과 대상' },
    expected: { type: 'string', description: '통과 조건. 수치는 단위와 측정 조건까지' },
    decided_by: { type: 'integer', enum: [0, 1, 2], description: '0 도구, 1 Sonnet 체크리스트, 2 Opus 의미 검증' },
    new_test: { type: 'boolean' },
  },
}

const TASK = {
  type: 'object',
  additionalProperties: false,
  required: ['id', 'title', 'spec', 'files', 'size', 'tier', 'tier_reason', 'blast_radius', 'risk_tags', 'depends_on', 'acceptance'],
  properties: {
    id: { type: 'string' },
    title: { type: 'string' },
    spec: { type: 'string' },
    files: { type: 'array', items: { type: 'string' } },
    size: { type: 'string', enum: ['S', 'M', 'L'] },
    tier: { type: 'string', enum: ['sonnet', 'opus'] },
    tier_reason: { type: 'string' },
    blast_radius: { type: 'string', enum: ['low', 'medium', 'high'] },
    risk_tags: { type: 'array', items: { type: 'string', enum: ['security', 'concurrency', 'migration', 'public-api'] } },
    depends_on: { type: 'array', items: { type: 'string' } },
    acceptance: { type: 'array', items: CRITERION },
  },
}

const PLAN_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['tasks'],
  properties: { tasks: { type: 'array', items: TASK } },
}

const IMPL_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['status', 'changed_files', 'verification', 'unverified', 'notes'],
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    changed_files: { type: 'array', items: { type: 'string' } },
    verification: { type: 'string' },
    unverified: { type: 'string' },
    notes: { type: 'string' },
  },
}

const L1_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['gate', 'criteria', 'unexpected_files', 'spec_gaps', 'overall', 'summary'],
  properties: {
    gate: {
      type: 'object',
      additionalProperties: false,
      required: ['result', 'failed'],
      properties: {
        result: { type: 'string', enum: ['pass', 'fail', 'error', 'partial', 'skip'], description: 'gate.sh 첫 줄 GATE RESULT 값 그대로' },
        failed: { type: 'array', items: { type: 'string' } },
      },
    },
    criteria: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['id', 'verdict', 'evidence'],
        properties: {
          id: { type: 'string' },
          verdict: { type: 'string', enum: ['pass', 'fail', 'unsure'] },
          evidence: { type: 'string' },
        },
      },
    },
    unexpected_files: { type: 'array', items: { type: 'string' } },
    spec_gaps: { type: 'array', items: { type: 'string' } },
    overall: { type: 'string', enum: ['pass', 'fail', 'unsure', 'inconclusive'] },
    summary: { type: 'string' },
  },
}

const L2_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['verdict', 'criteria', 'defects', 'guidance'],
  properties: {
    verdict: { type: 'string', enum: ['approve', 'reject'] },
    criteria: {
      type: 'array',
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['id', 'verdict', 'evidence'],
        properties: {
          id: { type: 'string' },
          verdict: { type: 'string', enum: ['pass', 'fail'] },
          evidence: { type: 'string', description: '파일:줄, 실행한 명령과 출력 등 구체적 근거' },
        },
      },
    },
    defects: {
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
    guidance: { type: 'string', description: 'reject일 때 다음 구현자가 따를 수정 방향' },
  },
}

const REVIEW_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['verdict', 'findings', 'constraints_for_next_tasks'],
  properties: {
    verdict: { type: 'string', enum: ['approve', 'changes_requested'] },
    findings: L2_SCHEMA.properties.defects,
    constraints_for_next_tasks: { type: 'array', items: { type: 'string' } },
  },
}

// ---- 입력과 상태 ---------------------------------------------------------------
const goal =
  typeof args === 'string' ? args
  : args && typeof args.goal === 'string' ? args.goal
  : ''

if (!goal) {
  return { status: 'invalid_input', error: '목표가 비어 있습니다. 예: /feature "지형 타일 로딩에 LOD 전환 추가"' }
}

const FEATURE_SLUG = slugOf(goal)

const state = {
  stage: 'init',
  recon: '',
  design: '',
  tasks: [],
  results: [],
  currentTask: null,
  constraints: [],
  unresolved: [],
  infraEvents: [],
  planWarnings: [],
  weight: { opus: 0, sonnet: 0 },   // 구현 시도별 크기 가중치 누적
  ratioFlagged: false,
}

try {
  return await run()
} catch (e) {
  if (!e || !e.halt) throw e
  const finished = new Set(state.results.map(r => r.id))
  return {
    status: e.status || 'halted',
    halted_at: e.step,
    reason: e.message,
    stage: state.stage,
    in_progress_task: state.currentTask,
    finished_tasks: state.results,
    not_started: state.tasks.filter(t => !finished.has(t.id) && t.id !== state.currentTask).map(t => t.id),
    impl_ratio: ratioReport(),
    infra_events: state.infraEvents,
    next: e.status === 'plan_invalid'
      ? '계획이 검사를 통과하지 못했다. plan_errors를 보고 목표를 더 구체적으로 나누거나 기준을 명시해 다시 실행할 것.'
      : '/feature를 새로 실행하지 말 것(설계부터 다시 돈다). 원인이 서버 오류면 잠시 뒤 같은 세션에서 이 run을 같은 스크립트로 relaunch하면, 완료된 agent는 저장된 결과를 재사용하고 멈춘 지점부터 재개된다.',
    ...(e.planErrors ? { plan_errors: e.planErrors } : {}),
  }
}

// ============================================================================

async function run() {
  // ---- 0. Explore (Sonnet) --------------------------------------------------
  phase('Design')
  state.stage = 'explore'
  state.recon = await step(
    `다음 목표와 관련된 코드베이스 현황을 조사하라. 설계자와 태스크 분해자가 파일을 다시 뒤지지 않아도 되도록 관련 파일, 핵심 시그니처, 기존 관례(좌표계·단위 표기 포함), 테스트 위치와 실행 명령, .claude/gate.conf에 설정된 게이트를 정리하라. 코드는 수정하지 말 것.\n\n목표:\n${goal}`,
    { agentType: 'Explore', label: 'explore', phase: 'Design' },
    r => (typeof r === 'string' && r.trim().length > 0) || '탐색 결과가 비어 있음',
  )

  // ---- 1. Design (Opus) -----------------------------------------------------
  state.stage = 'design'
  state.design = await step(
    `다음 목표에 대한 아키텍처 설계 문서를 작성하라. 탐색 결과로 충분한 부분은 파일을 다시 읽지 말 것. 검증 전략은 실행 가능한 명령과 수치 기준으로 쓸 것.\n\n목표:\n${goal}\n\n탐색 결과(Explore):\n${state.recon}`,
    { agentType: 'architect', label: 'architect', phase: 'Design' },
    d => (typeof d === 'string' && d.includes(DESIGN_END_MARKER)) || '설계 문서에 완결 마커가 없음 (출력이 잘렸을 가능성)',
  )

  // ---- 2. Plan (Opus) + 모델 없는 계획 검사 ------------------------------------
  phase('Plan')
  state.stage = 'plan'
  let plan = await step(
    `[LEAD: plan ${FEATURE_SLUG}]\n아래 설계 문서를 구현 태스크로 분해하라. 각 태스크에 size, tier(기본 sonnet), tier_reason, blast_radius, risk_tags, 구조화된 인수 조건(acceptance)을 지정하라. 모든 태스크에는 decided_by 0인 test·command·benchmark 조건이 1개 이상 있어야 한다. 처음부터 opus로 배정하는 몫은 크기 가중치(S=1, M=2, L=4) 합의 ${pct(PLAN_OPUS_TARGET)} 이내를 목표로 하라.\n\n목표:\n${goal}\n\n탐색 결과(Explore):\n${state.recon}\n\n설계 문서:\n${state.design}`,
    { agentType: 'lead', label: 'lead:plan', phase: 'Plan', schema: PLAN_SCHEMA },
    p => (Array.isArray(p.tasks) && p.tasks.length > 0) || '태스크 목록이 비어 있음',
  )

  let lint = lintPlan(plan.tasks)
  for (let r = 1; lint.errors.length > 0 && r <= PLAN_REVISIONS; r++) {
    log(`계획 검사 실패 ${lint.errors.length}건 → lead 수정 요청 ${r}/${PLAN_REVISIONS}`)
    plan = await step(
      `[LEAD: revise ${FEATURE_SLUG}]\n계획이 자동 검사를 통과하지 못했다. 아래 오류를 고쳐 전체 계획을 다시 출력하라. 지적되지 않은 부분은 바꾸지 말 것.\n\n오류:\n- ${lint.errors.join('\n- ')}\n\n이전 계획:\n${JSON.stringify(plan, null, 2)}\n\n설계 문서:\n${state.design}`,
      { agentType: 'lead', label: `lead:revise#${r}`, phase: 'Plan', schema: PLAN_SCHEMA },
      p => (Array.isArray(p.tasks) && p.tasks.length > 0) || '태스크 목록이 비어 있음',
    )
    lint = lintPlan(plan.tasks)
  }
  if (lint.errors.length > 0) {
    const err = new Error(`계획이 ${PLAN_REVISIONS}회 수정 후에도 검사를 통과하지 못함`)
    err.halt = true; err.step = 'plan-lint'; err.status = 'plan_invalid'; err.planErrors = lint.errors
    throw err
  }
  state.planWarnings = lint.warnings
  state.tasks = plan.tasks
  log(`태스크 ${plan.tasks.length}개, 계획 단계 Opus 배정 ${pct(lint.opusShare)} (목표 ≤ ${pct(PLAN_OPUS_TARGET)}, 상한 ${pct(PLAN_OPUS_MAX)})`)
  lint.warnings.forEach(w => log(`[계획 경고] ${w}`))

  // ---- 3. Implement + Verify --------------------------------------------------
  phase('Implement')
  state.stage = 'implement'
  const statusById = {}
  let doneSinceReview = []

  for (let i = 0; i < state.tasks.length; i++) {
    const task = state.tasks[i]
    state.currentTask = task.id

    const blockedBy = (task.depends_on || []).filter(d => statusById[d] !== 'done')
    const r = blockedBy.length > 0
      ? { id: task.id, title: task.title, status: 'skipped', reason: `선행 태스크 미완료: ${blockedBy.join(', ')}` }
      : await implementWithEscalation(task)

    statusById[task.id] = r.status
    state.results.push(r)
    state.currentTask = null
    log(`${task.id} ${r.status}${r.status === 'skipped' ? ` — ${r.reason}` : ` (${r.final_tier}, 구현 ${r.attempts}회, 검증 ${r.verified_by || '-'})`} · 구현 Opus 비율 추정 ${pct(opusShare())}`)
    checkRatio()

    if (r.status === 'done') doneSinceReview.push(task)
    const isLast = i === state.tasks.length - 1
    if (MID_REVIEW_EVERY > 0 && doneSinceReview.length >= MID_REVIEW_EVERY && !isLast) {
      await midReview(doneSinceReview)
      doneSinceReview = []
    }
  }

  // ---- 4. Final review (Opus, 새 컨텍스트) --------------------------------------
  const notDone = state.results.filter(r => r.status !== 'done')
  if (notDone.length > 0 || state.unresolved.length > 0) {
    return {
      status: 'incomplete',
      goal,
      final_review: 'not_run',
      not_done: notDone,
      unresolved_mid_review: state.unresolved,
      tasks: state.results,
      impl_ratio: ratioReport(),
      plan_warnings: state.planWarnings,
      infra_events: state.infraEvents,
    }
  }

  phase('Review')
  state.stage = 'final-review'
  const review = await step(
    `최종 검토. 이 기능이 아직 출하되면 안 되는 이유를 찾아라. 먼저 bash .claude/scripts/gate.sh로 전체 게이트를 다시 돌리고, 전체 git diff를 설계 문서·계획의 인수 조건과 대조하라. 특히 태스크 사이 접합부의 결함(좌표계·단위·축 순서 불일치, 스레드 경계, 초기화 순서)과 너무 약했던 인수 조건을 찾아라.\n\n목표:\n${goal}\n\n설계 문서:\n${state.design}\n\n계획(태스크 spec과 인수 조건):\n${JSON.stringify(state.tasks, null, 2)}\n\n태스크 결과 상태:\n${JSON.stringify(state.results.map(r => ({ id: r.id, status: r.status })), null, 2)}`,
    { agentType: 'final-reviewer', label: 'final-review', phase: 'Review', schema: REVIEW_SCHEMA },
  )

  return {
    status: review.verdict === 'approve' ? 'complete' : 'changes_requested',
    goal,
    verdict: review.verdict,
    findings: review.findings,
    tasks: state.results,
    impl_ratio: ratioReport(),
    plan_warnings: state.planWarnings,
    infra_events: state.infraEvents,
    design: state.design,
  }
}

// ---- 공통: 모든 agent 호출은 step()을 거친다 ---------------------------------
// 반환값은 항상 유효한 결과이고 null이 아니다. 모델은 agentType에 맞는 버전 고정 ID를 항상 지정한다.
async function step(prompt, opts, validate) {
  const model = MODEL_FOR[opts.agentType]
  if (!model) throw new Error(`모델 매핑이 없는 agentType: ${opts.agentType}`)
  let reason = ''
  for (let i = 0; i <= INFRA_RETRIES; i++) {
    const label = i === 0 ? opts.label : `${opts.label}~retry${i}`
    const note = i === 0 ? '' : retryNote(reason)
    reason = ''
    let out = null
    try {
      out = await agent(prompt + note, { ...opts, label, model })
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
  const err = new Error(`${opts.label}: 연속 ${INFRA_RETRIES + 1}회 실패 — ${reason}`)
  err.halt = true
  err.step = opts.label
  throw err
}

function retryNote(reason) {
  return `\n\n(참고: 이 단계의 직전 실행이 받아들여지지 않았다. 사유: ${reason}. ` +
    '파일을 수정하는 작업이라면 직전 실행이 남긴 부분 변경이 있을 수 있으니 git status와 git diff로 현재 상태부터 확인하라.)'
}

// ---- 계획 검사 (모델 없음) -----------------------------------------------------
function lintPlan(tasks) {
  const errors = []
  const warnings = []
  const seen = new Set()
  const critIds = new Set()
  let total = 0
  let opus = 0

  tasks.forEach((t, idx) => {
    const where = `${t.id}`
    if (seen.has(t.id)) errors.push(`${where}: 태스크 id 중복`)
    ;(t.depends_on || []).forEach(d => {
      if (!seen.has(d)) errors.push(`${where}: depends_on ${d}가 앞선 태스크가 아님(순서를 바꾸거나 id를 확인)`)
    })
    seen.add(t.id)

    const w = SIZE_WEIGHT[t.size] || 0
    total += w
    if (startsOnOpus(t)) opus += w
    if (t.tier === 'opus' && (!t.tier_reason || /^default$/i.test(t.tier_reason.trim())))
      errors.push(`${where}: tier가 opus인데 tier_reason이 없음`)
    if (t.blast_radius === 'high' && t.tier !== 'opus')
      warnings.push(`${where}: blast_radius high라 처음부터 Opus로 간다(tier_reason 없이 Opus 배정)`)

    const acc = t.acceptance || []
    if (acc.length === 0) errors.push(`${where}: 인수 조건이 없음`)
    const toolDecided = acc.filter(a => a.decided_by === 0 && ['test', 'command', 'benchmark'].includes(a.kind))
    if (acc.length > 0 && toolDecided.length === 0)
      errors.push(`${where}: 도구로 판정하는 조건(decided_by 0, kind test·command·benchmark)이 하나도 없음`)

    acc.forEach(a => {
      const aw = `${where}/${a.id}`
      if (!a.id || !a.id.startsWith(`${t.id}-`)) errors.push(`${aw}: 조건 id는 "${t.id}-A<번호>" 형식이어야 함`)
      if (critIds.has(a.id)) errors.push(`${aw}: 조건 id 중복`)
      critIds.add(a.id)
      if (!a.check || a.check.trim().length < 3) errors.push(`${aw}: check가 비어 있음`)
      if (!a.expected || a.expected.trim().length < 3) errors.push(`${aw}: expected가 비어 있음`)
      if ((a.kind === 'test' || a.kind === 'command') && a.decided_by !== 0)
        errors.push(`${aw}: ${a.kind} 조건은 decided_by 0이어야 함`)
      if (a.kind === 'inspection' && a.decided_by === 0)
        errors.push(`${aw}: inspection은 도구가 판정할 수 없음(decided_by 1 이상)`)
      if (a.kind === 'benchmark' && !/\d/.test(a.expected || ''))
        errors.push(`${aw}: benchmark의 expected에 수치가 없음`)
      if (/(정상\s*동작|문제\s*없|적절히|잘\s*동작|works?\s+(correctly|properly|fine))/i.test(a.expected || ''))
        errors.push(`${aw}: expected가 판정 불가능한 표현임("${a.expected}")`)
    })
  })

  const opusShare = total > 0 ? opus / total : 0
  if (opusShare > PLAN_OPUS_MAX)
    errors.push(`계획 단계 Opus 배정이 크기 가중치 기준 ${pct(opusShare)}로 상한 ${pct(PLAN_OPUS_MAX)}를 넘음. tier 기준에 명확히 해당하지 않는 태스크를 sonnet으로 낮출 것`)
  else if (opusShare > PLAN_OPUS_TARGET)
    warnings.push(`계획 단계 Opus 배정 ${pct(opusShare)}가 목표 ${pct(PLAN_OPUS_TARGET)}를 넘음(상한 이내)`)

  return { errors, warnings, opusShare }
}

// ---- 구현 + 검증 + 승격 ------------------------------------------------------
async function implementWithEscalation(task) {
  let tier = startsOnOpus(task) ? 'opus' : 'sonnet'
  let reason = tier === 'opus' ? 'tier-opus' : null   // senior-implementer로 가는 승격 조건
  let replanned = false
  const history = []   // 작업 실패만 쌓인다. 인프라 실패는 step()이 흡수한다.

  for (;;) {
    const n = history.length + 1
    const tagLine = tier === 'opus' ? `[ESCALATION: ${reason} ${task.id}]` : `[TASK: ${task.id}]`
    const impl = await step(buildImplPrompt(task, history, tagLine, tier), {
      agentType: tier === 'opus' ? 'senior-implementer' : 'implementer',
      label: `${task.id}:${tier}#${n}`,
      phase: 'Implement',
      schema: IMPL_SCHEMA,
    })
    state.weight[tier] += SIZE_WEIGHT[task.size] || 1

    let failure
    let guidance = ''
    let level = 0
    if (impl.status === 'blocked') {
      failure = `구현자 blocked 보고: ${impl.notes}`
    } else {
      const v = await verify(task, impl.changed_files, tier, `${n}`)
      if (v.pass) {
        return {
          id: task.id, title: task.title, status: 'done',
          final_tier: tier, attempts: n, verified_by: v.level === 2 ? 'L2(Opus)' : 'L1(Sonnet)',
          ...(v.reasons && v.reasons.length ? { l2_reasons: v.reasons } : {}),
          ...(reason ? { escalation: reason } : {}),
          ...(replanned ? { replanned: true } : {}),
        }
      }
      failure = v.failure
      guidance = v.guidance || ''
      level = v.level
    }
    history.push({ attempt: n, tier, impl, failure, guidance, level })

    if (tier === 'sonnet') {
      const sonnetFails = history.filter(h => h.tier === 'sonnet').length
      if (impl.status === 'blocked' || sonnetFails >= MAX_SONNET_ATTEMPTS) {
        tier = 'opus'
        reason = impl.status === 'blocked' ? 'blocked' : 'failed-2x'
        log(`${task.id}: Sonnet ${sonnetFails}회 실패${impl.status === 'blocked' ? '(blocked)' : ''} → Opus로 승격`)
      }
      continue
    }

    if (replanned) break
    const opusFails = history.filter(h => h.tier === 'opus').length
    if (impl.status === 'blocked' || opusFails >= MAX_OPUS_ATTEMPTS) {
      log(`${task.id}: Opus ${opusFails}회 실패${impl.status === 'blocked' ? '(blocked)' : ''} → lead replan`)
      const newTask = await step(
        `[LEAD: replan ${task.id}]\n태스크 ${task.id}가 senior-implementer에서도 실패했다. 실패 원인을 분석해 이 태스크 하나만 다시 써라. id는 ${task.id}로 유지하고, 무엇이 틀렸고 올바른 접근이 무엇인지 spec에 명시하라. 인수 조건 규칙은 계획 때와 같다.\n\n현재 태스크:\n${JSON.stringify(task, null, 2)}\n\n실패 이력:\n${history.map(h => `[시도 ${h.attempt}, ${h.tier}] ${h.failure}${h.guidance ? `\n검증자 소견: ${h.guidance}` : ''}`).join('\n\n')}\n\n설계 문서:\n${state.design}`,
        { agentType: 'lead', label: `lead:replan:${task.id}`, phase: 'Implement', schema: TASK },
        t => {
          if (t.id !== task.id) return `replan 결과의 id가 ${task.id}가 아님`
          const l = lintPlan([{ ...t, depends_on: [] }])
          return l.errors.filter(e => !e.startsWith('계획 단계')).length === 0 || `replan 태스크가 인수 조건 규칙을 어김: ${l.errors.join('; ')}`
        },
      )
      Object.assign(task, newTask, { depends_on: task.depends_on })
      replanned = true
      reason = 'replanned'
      continue
    }
  }

  const last = history[history.length - 1]
  return {
    id: task.id, title: task.title, status: 'failed',
    final_tier: tier, attempts: history.length,
    ...(reason ? { escalation: reason } : {}),
    ...(replanned ? { replanned: true } : {}),
    last_failure: last.failure,
    ...(last.guidance ? { last_guidance: last.guidance } : {}),
  }
}

// ---- 검증: 0단계는 훅과 L1이 도구로, 1단계 Sonnet, 2단계 Opus ---------------------
async function verify(task, changedFiles, implTier, tag) {
  const l1 = await step(buildL1Prompt(task, changedFiles), {
    agentType: 'verifier-l1', label: `${task.id}:L1#${tag}`, phase: 'Implement', schema: L1_SCHEMA,
  }, v => {
    if (v.gate.result === 'error') return `0단계 게이트를 실행하지 못함(도구 설치·환경 문제): ${v.gate.failed.join(', ') || v.summary}`
    if (v.overall === 'inconclusive') return `1단계 검증 불가(inconclusive): ${v.summary}`
    const missing = task.acceptance.map(a => a.id).filter(id => !v.criteria.some(c => c.id === id))
    return missing.length === 0 || `1단계 결과에 인수 조건 판정이 빠짐: ${missing.join(', ')}`
  })

  const l1Fails = l1.criteria.filter(c => c.verdict === 'fail')
  if (l1.gate.result === 'fail' || l1Fails.length > 0 || l1.overall === 'fail' || l1.spec_gaps.length > 0) {
    return { pass: false, level: 1, failure: describeL1(l1) }
  }

  const reasons = l2Reasons(task, changedFiles, implTier, l1)
  if (reasons.length === 0) return { pass: true, level: 1 }

  const l2 = await step(buildL2Prompt(task, changedFiles, implTier, l1, reasons), {
    agentType: 'verifier-l2', label: `${task.id}:L2#${tag}`, phase: 'Implement', schema: L2_SCHEMA,
  }, v => {
    if (v.verdict !== 'approve') return true
    const weak = task.acceptance.map(a => a.id).filter(id => {
      const c = v.criteria.find(x => x.id === id)
      return !c || c.verdict !== 'pass' || !c.evidence || c.evidence.trim().length < 10
    })
    return weak.length === 0 || `승인했지만 근거가 없거나 통과하지 않은 인수 조건이 있음: ${weak.join(', ')}`
  })

  if (l2.verdict === 'approve') return { pass: true, level: 2, reasons }
  return { pass: false, level: 2, failure: describeL2(l2), guidance: l2.guidance, reasons }
}

// 1단계로 끝낼 수 없는 이유. 비어 있으면 저위험이다.
function l2Reasons(task, changedFiles, implTier, l1) {
  const r = []
  if (implTier === 'opus') r.push('opus-implementation')
  if ((task.risk_tags || []).length > 0) r.push(`risk-tags:${task.risk_tags.join(',')}`)
  if (task.blast_radius !== 'low') r.push(`blast-radius:${task.blast_radius}`)
  const files = uniq([...(task.files || []), ...(changedFiles || []), ...(l1.unexpected_files || [])])
  const hits = uniq(files.flatMap(f => RISK_PATHS.filter(p => p.re.test(f)).map(p => `${p.tag}:${f}`)))
  if (hits.length > 0) r.push(`risk-path:${hits.slice(0, 5).join(',')}`)
  if (task.acceptance.some(a => a.decided_by === 2)) r.push('criteria-decided-by-2')
  if (l1.overall === 'unsure' || l1.criteria.some(c => c.verdict === 'unsure')) r.push('l1-unsure')
  if (l1.gate.result !== 'pass') r.push(`gate-${l1.gate.result}`)
  if ((l1.unexpected_files || []).length > 0) r.push('unexpected-files')
  return r
}

// ---- 중간 검토 (Opus) ---------------------------------------------------------
async function midReview(batch) {
  const at = batch[batch.length - 1].id
  state.stage = `mid-review@${at}`

  const mid = await step(
    `중간 검토. 최근 완료된 태스크(${batch.map(t => t.id).join(', ')})의 변경(git diff)이 설계 문서와 일치하는지 확인하라. 이후 태스크에 적용할 제약이 있으면 constraints_for_next_tasks에 적어라.\n\n설계 문서:\n${state.design}\n\n해당 태스크:\n${JSON.stringify(batch.map(t => ({ id: t.id, spec: t.spec, files: t.files })), null, 2)}`,
    { agentType: 'mid-reviewer', label: `mid-review@${at}`, phase: 'Implement', schema: REVIEW_SCHEMA },
  )
  state.constraints.push(...(mid.constraints_for_next_tasks || []))

  const blocking = (mid.findings || []).filter(f => f.severity !== 'minor')
  if (blocking.length > 0) {
    const fixTask = {
      id: `fix@${at}`,
      title: `중간 검토 지적 수정 (${at})`,
      spec: `중간 검토에서 나온 지적을 수정하라. 설계 문서의 인터페이스 계약과 불변 조건을 유지할 것.\n\n지적:\n${JSON.stringify(blocking, null, 2)}`,
      files: uniq(blocking.map(f => f.file).concat(batch.flatMap(t => t.files))),
      size: 'M',
      blast_radius: 'medium',
      risk_tags: uniq(batch.flatMap(t => t.risk_tags || [])),
      acceptance: batch.flatMap(t => t.acceptance),
    }
    const fix = await step(
      buildImplPrompt(fixTask, [], `[ESCALATION: mid-review-fix ${at}]`, 'opus'),
      { agentType: 'senior-implementer', label: `fix@${at}`, phase: 'Implement', schema: IMPL_SCHEMA },
    )
    state.weight.opus += SIZE_WEIGHT.M

    let unresolvedReason = null
    if (fix.status === 'blocked') {
      unresolvedReason = `수정자 blocked 보고: ${fix.notes}`
    } else {
      const v = await verify(fixTask, fix.changed_files, 'opus', 'fix')
      if (!v.pass) unresolvedReason = v.failure
    }
    if (unresolvedReason) {
      state.unresolved.push({ at, findings: blocking, reason: unresolvedReason })
      log(`[미해결] mid-review@${at} 지적이 해소되지 않음 → 최종 status는 complete가 될 수 없음`)
    }
  }
  state.stage = 'implement'
}

// ---- 구현 비율 --------------------------------------------------------------
function opusShare() {
  const t = state.weight.opus + state.weight.sonnet
  return t > 0 ? state.weight.opus / t : 0
}

function checkRatio() {
  const s = opusShare()
  if (s > RATIO_MAX && !state.ratioFlagged) {
    state.ratioFlagged = true
    log(`[비율] 구현 Opus 비율 추정 ${pct(s)} > 허용 ${pct(RATIO_MAX)}. 정확성 우선으로 계속 진행하고 결과에 표시한다.`)
  }
}

function ratioReport() {
  const s = opusShare()
  const culprits = state.results
    .filter(r => r.final_tier === 'opus')
    .map(r => ({ id: r.id, why: r.escalation || 'tier-opus', attempts: r.attempts }))
  return {
    basis: '크기 가중치 추정(S=1, M=2, L=4). 실제 토큰 비율은 usage-by-model.ps1 [4]로 확인',
    opus_share: Math.round(s * 1000) / 10,
    target: RATIO_TARGET * 100,
    max: RATIO_MAX * 100,
    exceeded: s > RATIO_MAX,
    opus_tasks: culprits,
  }
}

// ---- 프롬프트 ----------------------------------------------------------------
function buildImplPrompt(task, history, tagLine, tier) {
  let p =
    `${tagLine}\n태스크 ${task.id}: ${task.title}\n\nspec:\n${task.spec}\n\n대상 파일:\n${(task.files || []).join('\n')}` +
    `\n\n인수 조건 (전부 만족해야 완료. new_test가 true인 테스트는 먼저 작성):\n${JSON.stringify(task.acceptance, null, 2)}` +
    `\n\n작업을 끝내기 전에 bash .claude/scripts/gate.sh 가 통과해야 한다(끝내려 하면 훅이 다시 확인한다).` +
    `\n\n설계 문서:\n${state.design}\n\n탐색 결과(Explore, 파일을 다시 뒤지기 전에 먼저 참고):\n${state.recon}`
  if (tier === 'opus') {
    p += '\n\n결과는 당신의 작업 맥락을 모르는 별도의 Opus 검증자가 결함을 찾는 관점으로 검토한다.'
  }
  if (state.constraints.length > 0) {
    p += `\n\n중간 검토에서 추가된 제약:\n- ${state.constraints.join('\n- ')}`
  }
  if (history.length > 0) {
    p += '\n\n이전 시도 실패 이력 (근본 원인부터 진단할 것):\n' + history
      .map(h => `[시도 ${h.attempt}, ${h.tier}, 검증 ${h.level ? `${h.level}단계` : '전'}]\n구현자 보고: ${JSON.stringify(h.impl)}\n실패 내용: ${h.failure}${h.guidance ? `\n검증자 소견(따를 것): ${h.guidance}` : ''}`)
      .join('\n\n')
  }
  return p
}

function buildL1Prompt(task, changedFiles) {
  return (
    `태스크 ${task.id} "${task.title}" 를 1단계 검증하라. 구현자의 보고는 주지 않는다. 코드와 도구 결과로만 판정하라.\n` +
    '먼저 bash .claude/scripts/gate.sh 를 실행하고 첫 줄 GATE RESULT 값을 gate.result에 그대로 옮겨라. ' +
    'decided_by 0 조건은 명령을 직접 실행해 판정하고, decided_by 2 조건은 unsure로 두어라. 근거 없는 pass는 쓰지 말 것.' +
    `\n\nspec:\n${task.spec}\n\n대상 파일:\n${(task.files || []).join('\n')}` +
    `\n\n구현자가 바꿨다고 한 파일(목록만. git status로 대조하고, 목록 밖 변경은 unexpected_files에):\n${(changedFiles || []).join('\n') || '(없음)'}` +
    `\n\n인수 조건:\n${JSON.stringify(task.acceptance, null, 2)}`
  )
}

function buildL2Prompt(task, changedFiles, implTier, l1, reasons) {
  return (
    `태스크 ${task.id} "${task.title}" 를 2단계 검증하라. 임무는 이 변경이 틀렸다는 증거를 찾는 것이다. ` +
    '승인은 결함을 찾으려 충분히 시도했는데도 찾지 못했을 때만 하고, 모든 인수 조건에 구체적 근거(파일:줄, 명령과 출력)를 붙여라. ' +
    `구현자의 설명은 주지 않는다. git diff로 코드를 직접 읽어라.${implTier === 'opus' ? ' 이 구현은 Opus가 했다. 같은 모델의 맹점을 공유하지 않도록, 구현자가 당연하게 여겼을 가정부터 의심하라.' : ''}` +
    `\n\n2단계로 올라온 이유: ${reasons.join(' | ')}` +
    `\n\nspec:\n${task.spec}\n\n대상 파일:\n${(task.files || []).join('\n')}\n\n바뀐 파일:\n${uniq([...(changedFiles || []), ...(l1.unexpected_files || [])]).join('\n') || '(없음)'}` +
    `\n\n위험 태그: ${(task.risk_tags || []).join(', ') || '없음'}\n\n인수 조건:\n${JSON.stringify(task.acceptance, null, 2)}` +
    `\n\n1단계 결과(참고용. 믿지 말고 의심스러우면 다시 실행):\n${JSON.stringify(l1, null, 2)}` +
    `\n\n설계 문서:\n${state.design}`
  )
}

// ---- helpers ----------------------------------------------------------------
function startsOnOpus(task) {
  return task.tier === 'opus' || task.blast_radius === 'high'
}

function describeL1(l1) {
  const parts = [`[1단계] ${l1.summary}`, `게이트: ${l1.gate.result}${l1.gate.failed.length ? ` (${l1.gate.failed.join(', ')})` : ''}`]
  l1.criteria.filter(c => c.verdict === 'fail').forEach(c => parts.push(`${c.id} 실패: ${c.evidence}`))
  l1.spec_gaps.forEach(g => parts.push(`spec 누락: ${g}`))
  return parts.join('\n')
}

function describeL2(l2) {
  const parts = ['[2단계] 거부']
  l2.criteria.filter(c => c.verdict === 'fail').forEach(c => parts.push(`${c.id} 실패: ${c.evidence}`))
  l2.defects.forEach(d => parts.push(`[${d.severity}] ${d.file}: ${d.issue} → ${d.fix}`))
  return parts.join('\n')
}

function uniq(a) {
  return a.filter((x, i) => x && a.indexOf(x) === i)
}

function pct(x) {
  return `${Math.round(x * 1000) / 10}%`
}

function slugOf(s) {
  const ascii = s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 30)
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0
  return (ascii || 'feature') + '-' + h.toString(16)
}
