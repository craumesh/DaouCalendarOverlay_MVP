# 모델 라우팅 정책

메인 세션(Opus)은 조율자다. 직접 탐색하거나 구현하지 않고, 아래 규칙대로 위임한다.

`lead`, `implementer`, `senior-implementer` 호출은 `route-guard` 훅이 검사한다. 태그가 없거나 조건에 맞지 않으면 호출이 차단되고 이유가 돌아온다. 차단되면 태그만 바꿔 다시 시도하지 말고, 차단 이유가 가리키는 에이전트로 바꾼다. 사실과 다른 태그는 붙이지 않는다.

## 누가 무엇을 하는가

| 에이전트 | 모델 | 언제 |
|---|---|---|
| `Explore` | Sonnet | **모든 탐색.** 파일·심볼 찾기, 구조 파악, 어디서 무엇을 처리하는지, 변경 영향 범위, 관련 테스트. |
| `architect` | Fable | 새 기능의 최초 설계. 기능당 1회. |
| `lead` | Opus | **기능당 1회** 태스크 분해. 예외는 아래 replan 1회뿐. |
| `implementer` | Sonnet | **모든 구현의 첫 시도.** 난이도와 상관없이 여기부터 시작한다. |
| `senior-implementer` | Opus | **승격 조건 네 가지 중 하나일 때만.** |
| `verifier` | Sonnet | 구현 후 독립 검증. 구현자의 자가 보고를 믿지 말고 항상 거친다. |
| `mid-reviewer` | Opus | 완료 태스크 3개마다 중간 검토. |
| `reviewer` | Fable | 최종 검토 1회. |

`general-purpose` 에이전트는 쓰지 않는다. 조사는 `Explore`, 구현은 `implementer`에게 맡긴다.

## 탐색 규칙

- 파일 위치를 모르거나, 3개 이상 파일을 읽어야 하거나, 검색이 필요하면 `Explore`에 맡긴다.
- 메인 세션이 직접 읽는 것은 경로를 이미 아는 파일 1~2개를 확인하는 정도까지다.
- `lead`나 `senior-implementer`를 탐색·질문·방향 확인 용도로 부르지 않는다.
- `Explore` 결과는 이후 `architect`, `lead`, `implementer` 위임 프롬프트에 그대로 붙인다. 같은 파일을 여러 에이전트가 다시 뒤지지 않게 한다.

## lead 규칙 (기능당 1회)

- 프롬프트 첫 줄은 `[LEAD: plan <기능-슬러그>]`다. 슬러그는 영문 소문자와 하이픈으로 쓴다(예: `user-invite`). 같은 슬러그로 두 번 부를 수 없다.
- 계획을 받은 뒤에는 구현 중 궁금한 점이나 방향 확인으로 `lead`를 다시 부르지 않는다. 계획의 spec과 acceptance를 따르고, 모호한 태스크도 일단 `implementer`에게 맡긴다. 막히면 승격 규칙을 따른다.
- 유일한 예외는 `[LEAD: replan <태스크ID>]`다. 그 태스크에서 `senior-implementer`가 2회 실패한 뒤 1회만 가능하다. replan 후에도 실패하면 멈추고 사용자에게 보고한다.

## 구현 라우팅

모든 구현은 `implementer`부터 시작하고, 프롬프트 첫 줄에 `[TASK: <태스크ID>]`를 붙인다. lead 계획의 id를 쓰고, 계획 없는 작은 작업이면 짧은 슬러그(예: `fix-login-typo`)를 쓴다.

`senior-implementer`는 아래 네 조건 중 하나일 때만 부른다. 프롬프트 첫 줄은 `[ESCALATION: <조건> <태스크ID>]`다.

| 조건 | 뜻 | 훅이 확인하는 것 |
|---|---|---|
| `tier-opus` | lead 계획에서 이 태스크가 tier=opus 또는 blast_radius=high | 이 세션에 lead 계획이 있음 |
| `failed-2x` | `implementer`가 `verifier` 검증에 2회 실패 | 같은 태스크의 implementer 호출이 2회 이상 |
| `blocked` | `implementer`가 STATUS: BLOCKED 보고 | 같은 태스크의 implementer 호출이 1회 이상 |
| `mid-review-fix` | `mid-reviewer`의 critical/major 지적 수정 | 이 세션에 mid-reviewer 호출이 있음 |

- "어려워 보인다", "중요하다", "파일이 많다"는 승격 조건이 아니다. 애매하면 `implementer`다. 실패하면 어차피 승격된다.
- 승격할 때는 이전 시도의 구현자 보고와 검증 실패 내용 **전문**을 위임 프롬프트에 넣는다.
- 서버 오류로 인한 중단은 실패 횟수에 넣지 않는다(아래 '실패 처리' 참고).

## 절차

1. **규모 판단.** 파일 3개 이상을 새로 만들거나 여러 모듈에 걸치는 작업은 "큰 작업"이다. 큰 작업은 사용자에게 `/feature "<목표>"` 실행을 제안한다. 워크플로에는 이 규칙이 코드로 고정돼 있다.
2. 큰 작업을 대화로 진행할 때: `Explore` → `architect` → `lead`(plan 1회) → 태스크별 `implementer` → `verifier`(승격 조건이면 `senior-implementer`) → 완료 3개마다 `mid-reviewer` → 마지막에 `reviewer`.
3. 작은 작업: 필요하면 `Explore` → `implementer` → `verifier`. `architect`와 `lead`는 쓰지 않는다.

## 실패 처리: 서버 오류와 작업 실패를 섞지 않는다

**인프라 실패** — 서브에이전트가 API 오류(529 overloaded, 5xx 등)로 중단된 경우. 결과가 "잘렸다/끝내지 못했다"는 안내가 붙은 부분 결과이거나 `Agent terminated early due to an API error` 오류로 온다. verifier의 INCONCLUSIVE(네트워크·테스트 인프라 문제로 검증 불가)도 여기에 속한다.
- 그 결과를 성공으로 취급하지 않고, 다음 단계로 넘어가지 않는다.
- 같은 에이전트에 같은 위임 프롬프트로 재시도하되 "직전 실행이 중단됐으니 git status/diff로 현재 상태부터 확인하라"를 덧붙인다.
- 에스컬레이션 실패 횟수에 넣지 않는다. 모델의 능력 문제가 아니다.
- 2회 재시도해도 안 되면 멈추고, 어느 단계에서 멈췄는지 사용자에게 보고한다.

**작업 실패** — verifier FAIL 또는 STATUS: BLOCKED. 이것만 위의 에스컬레이션 규칙을 따른다.

**`/feature` 워크플로 결과 해석**
- `status: complete`일 때만 완료로 보고한다.
- `status: halted` → 멈춘 단계(`halted_at`)와 원인을 보고한다. 원인이 서버 오류면 `/feature`를 새로 실행하지 말고(설계부터 다시 돈다), 같은 run을 같은 스크립트로 relaunch하자고 제안한다. relaunch하면 완료된 에이전트는 저장된 결과를 재사용한다.
- `status: incomplete` 또는 `changes_requested` → 실패·건너뛴 태스크와 미해결 지적을 그대로 보고한다. 완료로 요약하지 않는다.

## Fable 사용 상한

- `architect`는 기능당 1회, `reviewer`는 최종 검토 1회. 즉 기능당 Fable 호출은 최대 2회다. 중간 검토는 `mid-reviewer`(Opus)가 한다. 구현, 디버깅, 탐색에 Fable을 쓰지 않는다.

## 위임 프롬프트 작성 규칙

서브에이전트는 이 대화를 볼 수 없다. 위임할 때 다음을 반드시 넣는다:
- 첫 줄 태그 (`lead`, `implementer`, `senior-implementer`)
- 태스크 spec 전문 (lead가 쓴 그대로, 요약하지 말 것)
- 대상 파일 경로와 관련 `Explore` 결과
- acceptance 기준
- 설계 문서의 관련 섹션 (인터페이스, 불변 조건)
- 재시도나 승격이라면 이전 실패 이력 전문
