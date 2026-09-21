# 모델 라우팅 정책

이 프로젝트는 작업 성격에 따라 서브에이전트(=모델)를 나눠 쓴다. 메인 세션(Opus)은 조율자다. 메인 세션이 직접 코드를 많이 쓰지 말고, 아래 규칙대로 위임한다.

## 누가 무엇을 하는가

| 에이전트 | 모델 | 언제 |
|---|---|---|
| `architect` | Fable | 새 기능/모듈의 **최초 설계**. 구현 전 1회. |
| `lead` | Opus | 설계를 태스크로 분해, 각 태스크의 spec·tier·blast_radius·acceptance 지정. 하위 모델이 헤맬 때 방향 재설정. |
| `implementer` | Sonnet | **일반 구현 기본값.** spec이 명확한 구현, 테스트 작성, 국소 수정. |
| `senior-implementer` | Opus | tier=opus 또는 blast_radius=high 태스크, implementer가 2회 실패한 태스크, 작은 누락이 큰 재작업이 되는 작업. |
| `verifier` | Sonnet | 구현 후 독립 검증. 구현자 자가 보고를 믿지 말고 항상 거친다. |
| `reviewer` | Fable | **중간 검토와 최종 검토**만. |
| `Explore` | Sonnet | 코드베이스 탐색. |

## 절차

1. **규모 판단.** 파일 3개 이상을 새로 만들거나 여러 모듈에 걸치는 작업이면 "큰 작업"이다. 큰 작업은 사용자에게 `/feature "<목표>"` 워크플로 실행을 제안한다(에스컬레이션 루프가 코드로 고정돼 있어 더 안정적이다).
2. 큰 작업을 대화로 진행할 때는 순서를 지킨다: `architect` → `lead` → 태스크별 (`implementer` 또는 `senior-implementer`) → `verifier` → 태스크 3개마다 `reviewer` 중간 검토 → 마지막에 `reviewer` 최종 검토.
3. 작은 작업(단일 파일 수정, 명백한 버그 수정)은 `architect`/`lead` 없이 `implementer` → `verifier`만 거친다.

## 에스컬레이션 규칙 (반드시 지킬 것)

- `implementer`가 `verifier` 검증에 **2회** 실패하거나 `STATUS: BLOCKED`를 보고하면, 3번째 시도는 하지 말고 `senior-implementer`에게 넘긴다. 이때 이전 시도의 구현자 보고와 검증 실패 내용을 **전부** 위임 프롬프트에 포함한다.
- `senior-implementer`도 2회 실패하면 `lead`에게 spec 재작성을 요청한 뒤 다시 시도한다. 그래도 실패하면 사용자에게 보고하고 멈춘다.
- `lead`가 tier=opus 또는 blast_radius=high로 표시한 태스크는 처음부터 `senior-implementer`에게 준다.

## Fable 사용 상한

- `architect`는 기능당 1회. `reviewer`는 중간 검토 + 최종 검토. 그 외 용도로 Fable 에이전트를 호출하지 않는다. 구현, 디버깅, 탐색에 Fable을 쓰지 않는다.

## 위임 프롬프트 작성 규칙

서브에이전트는 이 대화를 볼 수 없다. 위임할 때 다음을 반드시 넣는다:
- 태스크 spec 전문 (`lead`가 쓴 그대로, 요약하지 말 것)
- 대상 파일 경로
- acceptance 기준
- 설계 문서의 관련 섹션 (인터페이스, 불변 조건)
- 재시도라면 이전 실패 이력 전문
