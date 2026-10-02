# 모델 라우팅 정책

이 프로젝트는 **Opus 5.5(`claude-opus-5-5`)와 Sonnet 5.5(`claude-sonnet-5-5`) 두 모델만** 쓴다. 모든 역할의 추론 수준은 `high`다. 다른 모델(Fable, Sonnet 5, Haiku 등)을 지정하거나 `model` 파라미터로 바꾸지 않는다.

메인 세션(Opus 5.5)은 조율자다. 직접 탐색하거나 구현하지 않고 아래 규칙대로 위임한다. `lead`, `implementer`, `senior-implementer` 호출은 `route-guard` 훅이 검사한다. 차단되면 태그만 바꿔 다시 시도하지 말고, 차단 이유가 가리키는 에이전트로 바꾼다. 사실과 다른 태그는 붙이지 않는다.

## 역할

| 에이전트 | 모델 | 언제 |
|---|---|---|
| `Explore` | Sonnet 5.5 | 모든 탐색. 파일·심볼 찾기, 구조, 영향 범위, 테스트·빌드 명령 |
| `architect` | Opus 5.5 | 새 기능의 최초 설계. 기능당 1회 |
| `lead` | Opus 5.5 | 기능당 1회 태스크 분해와 인수 조건 작성 |
| `implementer` | Sonnet 5.5 | 모든 구현의 첫 시도 |
| `senior-implementer` | Opus 5.5 | 승격 조건에 해당할 때만 |
| `verifier-l1` | Sonnet 5.5 | 1단계 검증. 모든 구현 뒤 가장 먼저 |
| `verifier-l2` | Opus 5.5 | 2단계 의미 검증과 최종 승인 (조건부) |
| `mid-reviewer` | Opus 5.5 | 완료 태스크 3개마다 중간 검토 |
| `final-reviewer` | Opus 5.5 | 기능 단위 최종 검토 1회, 새 컨텍스트 |

`general-purpose` 에이전트는 쓰지 않는다.

## 절차

1. **규모 판단.** 파일 3개 이상을 새로 만들거나 여러 모듈에 걸치면 큰 작업이다. 큰 작업은 사용자에게 `/feature "<목표>"` 실행을 제안한다. 워크플로에는 이 문서의 규칙이 코드로 고정돼 있다.
2. **큰 작업을 대화로 진행할 때:** `Explore` → `architect` → `lead`(plan 1회) → 태스크별 [`implementer` → 검증] → 완료 3개마다 `mid-reviewer` → `final-reviewer`.
3. **작은 작업:** 필요하면 `Explore` → `implementer` → 검증. `architect`와 `lead`는 쓰지 않는다. 인수 조건은 메인 세션이 아래 형식으로 직접 정해 위임 프롬프트에 넣는다.

## 탐색

- 파일 위치를 모르거나, 3개 이상 파일을 읽어야 하거나, 검색이 필요하면 `Explore`에 맡긴다. 메인 세션은 경로를 아는 파일 1~2개 확인까지만 직접 한다.
- `Explore` 결과는 이후 위임 프롬프트에 그대로 붙인다.

## 인수 조건

모든 태스크는 구조화된 인수 조건을 가진다.

| 필드 | 내용 |
|---|---|
| `id` | `T3-A1` 형식 |
| `kind` | test / command / benchmark / property / inspection |
| `check` | 정확한 명령이나 테스트 이름. inspection이면 볼 파일과 대상 |
| `expected` | 통과 조건. 수치는 단위와 측정 조건까지 |
| `decided_by` | 0 도구 / 1 Sonnet 체크리스트 / 2 Opus 의미 검증 |
| `new_test` | 구현자가 새로 작성해야 하는 테스트인지 |

- 모든 태스크에 `decided_by 0`인 test·command·benchmark 조건이 1개 이상 있어야 한다. inspection만 있는 태스크는 안 된다.
- "정상 동작", "문제없음", "적절히" 같은 판정 불가능한 표현은 쓰지 않는다.
- 태스크마다 `risk_tags`(security, concurrency, migration, public-api)를 정직하게 단다.

## 구현 라우팅

- `implementer`가 먼저 한다. 프롬프트 첫 줄은 `[TASK: <태스크ID>]`.
- `senior-implementer`는 아래 조건에서만 부른다. 프롬프트 첫 줄은 `[ESCALATION: <조건> <태스크ID>]`.

| 조건 | 뜻 |
|---|---|
| `tier-opus` | lead 계획에서 tier=opus 또는 blast_radius=high |
| `failed-2x` | `implementer`가 검증에 2회 실패 |
| `blocked` | `implementer`가 BLOCKED 보고 |
| `mid-review-fix` | `mid-reviewer`의 critical·major 지적 수정 |
| `replanned` | lead replan 이후의 재시도 |

- tier=opus 기준은 난이도가 아니라 **조용한 오류 영역**(좌표계 변환 코어, 대좌표 정밀도, 3D 포맷 변환 코어, 결정론·넷코드, 물리 코어)과 **되돌리기 비싼 결정**(공개 API·스키마·세이브 포맷, 동기화 프로토콜), **보안·동시성**이다. "어려워 보인다"는 사유가 아니다.
- 재시도하는 구현자에게는 이전 실패 내용과 검증자 소견을 전부 넣는다.
- `senior-implementer`가 2회 실패하거나 blocked면 `[LEAD: replan <태스크ID>]`로 그 태스크만 다시 쓰게 하고, `[ESCALATION: replanned <태스크ID>]`로 한 번 더 시도한다. 그래도 실패하면 멈추고 보고한다.
- **구현 비율 목표는 Opus:Sonnet = 3:7, 허용 최대 4:6**(구현 토큰 기준)이다. 넘을 것 같으면 승격을 막지는 않되, 어느 태스크 때문인지 보고한다.

## 검증 라우팅

**0단계 (도구):** 구현자가 끝내려 할 때 훅이 `bash .claude/scripts/gate.sh`를 돌리고, 실패하면 구현자에게 되돌려 보낸다. 명령은 `.claude/gate.conf`에 있다.

**1단계 (`verifier-l1`, Sonnet 5.5):** 게이트를 다시 돌리고 인수 조건과 spec 항목을 체크리스트로 판정한다(pass / fail / unsure).

**2단계 (`verifier-l2`, Opus 5.5):** 새 컨텍스트에서 결함을 찾는 적대적 관점으로 의미를 검증하고 최종 승인한다. 승인하려면 모든 인수 조건에 구체적 근거가 있어야 한다.

- **검증자 ≥ 구현자.** Sonnet 구현은 1단계 뒤 기본적으로 2단계까지 간다. 1단계로 끝낼 수 있는 것은 아래를 **전부** 만족하는 저위험 작업뿐이다.
  - Sonnet 구현, blast_radius low, risk_tags 없음
  - 바뀐 파일이 위험 경로(인증·암호화, 마이그레이션·직렬화, 좌표계·투영, 네트워크·동기화, 동시성, 공개 헤더·타입 정의, 3D 포맷·타일) 밖
  - `decided_by 2` 조건 없음
  - 게이트가 실제로 돌아 pass이고, 1단계가 모든 조건에 근거 있는 pass
- **무조건 2단계:** Opus 구현, 위험 태그나 위험 경로 변경, 1단계의 unsure, 게이트 partial·skip.
- **컨텍스트 분리.** 검증자와 검토자에게는 spec, 인수 조건, 바뀐 파일 목록만 준다. 구현자의 설명·자가 보고는 주지 않는다. Opus 구현을 Opus가 검증할 때는 구현 대화를 이어서 묻지 말고 반드시 새 `verifier-l2` 호출로 한다.
- 1단계에서 이미 fail이면 2단계를 부르지 않고 바로 재구현으로 간다.

## 실패 처리: 서버 오류와 작업 실패를 섞지 않는다

**인프라 실패:** 서브에이전트가 API 오류(529, 5xx)로 중단됐거나, 결과가 잘렸거나, 1단계가 inconclusive(도구·환경 문제로 확인 불가)이거나, 게이트가 error(명령 실행 불가)인 경우.
- 그 결과를 성공으로 취급하지 않고 다음 단계로 넘어가지 않는다.
- 같은 에이전트에 같은 위임 프롬프트로 재시도하되 "직전 실행이 중단됐으니 git status/diff로 현재 상태부터 확인하라"를 덧붙인다.
- 승격 횟수에 넣지 않는다.
- 2회 재시도해도 안 되면 멈추고 어느 단계에서 멈췄는지 보고한다.

**작업 실패:** 검증 fail 또는 BLOCKED. 이것만 승격으로 이어진다.

**`/feature` 결과 해석**
- `status: complete`일 때만 완료로 보고한다.
- `halted`: 멈춘 단계와 원인을 보고한다. 서버 오류면 `/feature`를 새로 실행하지 말고 같은 run을 같은 스크립트로 relaunch하자고 제안한다.
- `plan_invalid`: 계획 검사 오류(`plan_errors`)를 그대로 보고한다.
- `incomplete`, `changes_requested`: 실패·건너뛴 태스크와 미해결 지적을 그대로 보고한다. 완료로 요약하지 않는다.
- `impl_ratio.exceeded`가 true면 원인 태스크와 함께 보고한다.

## 위임 프롬프트

서브에이전트는 이 대화를 볼 수 없다. 위임할 때 반드시 넣는다.
- 첫 줄 태그 (`lead`, `implementer`, `senior-implementer`)
- 태스크 spec 전문(요약하지 말 것)과 대상 파일, 관련 `Explore` 결과
- 인수 조건 전체
- 설계 문서의 관련 섹션(인터페이스, 불변 조건, 좌표계·단위)
- 재시도나 승격이면 이전 실패 내용과 검증자 소견 전문
- 검증자에게는 위에서 구현자 보고를 뺀 것과 바뀐 파일 목록

## 이 프로젝트에 맞춘 부분 (DaouCalendarOverlay)

위 규칙은 범용 키트(claude-orchestration-starter)에서 왔다. 이 절은 .NET 10 WPF 오버레이, Chrome MV3 확장, Native Messaging/Named Pipe로 된 이 저장소에 맞춘 보충이다. 위 규칙과 충돌하면 위 규칙이 우선이다.

- **tier=opus에 해당하는 이 프로젝트의 영역.** 위의 "조용한 오류 영역"과 "되돌리기 비싼 결정"은 여기서 다음을 뜻한다.
  - 브리지 프로토콜: getConfig/postResult 스키마, `ProtocolVersion`, 파이프 이름
  - 쿠키·세션·확장 권한과 `PipePeerVerifier`
  - 사용자 데이터·레지스트리 삭제(제거 경로)
  - 캐시·settings.json 포맷과 역호환
  - 프로세스 진입점(`Program.Main`의 GUI/host/제거 분기)
  - 파이프 서버 동시성과 lease
  - 좌표계·3D·게임 항목은 이 프로젝트에 해당하지 않는다.
- **0단계 게이트.** `.claude/gate.conf`에 있다.
  - C#: `dotnet build DaouCalendarOverlay.sln -c Release -warnaserror` + `dotnet test`
  - JS(확장 worker): `node --check`
  - 항상: `.claude/scripts/gate-extra.sh`
    - `tools/check-html.js`로 기술 문서 HTML 형식을 점검한다.
    - .cs 없이 문서, worker, csproj만 바뀌었으면 빌드와 테스트를 돈다.
- **위험 경로.** `feature.js`의 `RISK_PATHS`를 이 프로젝트의 파일 이름에 맞췄다.
- **확장 버전 상향.** worker를 바꾸면 기술 문서 §24.5 체크리스트를 따른다: manifest, worker 파일 이름, EmbeddedResource, Installer의 Files/Expected/obsolete, 테스트 리터럴, CHANGELOG.
- **기술 문서 편집 규약.**
  - 절 번호와 id를 유지한다.
  - 코드 발췌는 현재 파일에서 메서드 경계로 다시 복사한다.
  - `v7.0.0` 문자열을 쓰지 않는다.
  - 정본 절 규칙과 사실 대장(검증 가능한 사실마다 근거)을 따른다.
- **사용자에게 보이는 글.** 보고, 중간 안내, 명령 설명은 모두 한국어로 쓴다.
- **저장소 위치.** 저장소는 `D:\Develop\DaouCalendarOverlay_MVP`(HDD)다. 오버레이 배포본은 저장소 밖 `%LOCALAPPDATA%\Programs\DaouCalendarOverlay`에서 실행한다. 저장소 bin의 EXE를 실행해 두면 빌드할 때 파일이 잠긴다.
