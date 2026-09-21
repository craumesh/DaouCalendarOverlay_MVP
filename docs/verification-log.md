# 검증 기록 (verification-log)

작업 지시서 `docs/OPUS_WORK_ORDER_2026-09-21.md` 6절 #7에 따라 수동/독립 검증 결과를 남긴다. 각 항목은 날짜·환경·결과를 포함한다.

## 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro 10.0.26200 |
| .NET SDK | 10.0.401 (프로젝트 TFM net8.0-windows) |
| 저장소 | `C:\Users\USER\Develop\DaouCalendarOverlay_MVP` (main) |
| 기준 커밋 | `a046a1b` = 태그 `v7.0.0-asbuilt` |

## P0

| 날짜 | 작업 | 검증 방법 | 결과 |
|---|---|---|---|
| 2026-09-21 | T0.1 | `git log --decorate`, `git status` | 태그 `v7.0.0-asbuilt`가 `a046a1b`에 부여됨. docs/·.claude/·CLAUDE.md 커밋 `73ba742`. 작업 트리 깨끗함. |
| 2026-09-21 | T0.1 빌드 기준선 | `dotnet build -c Release` | 경고 0 / 오류 0, 3.9초 |
| 2026-09-21 | T0.2-T1 테스트 골격 | verifier(Sonnet) 독립 실행: sln list, build, test, csproj 대조, 금지 파일 diff | PASS. 테스트 1건(프로토콜 상수 가드) 통과. 메인 csproj·publish.ps1 무변경. |
| 2026-09-21 | T0.2-T2 순수 로직 추출 | senior-implementer 동등성 하니스(원본 본문 복사본 vs 새 `CalendarGrid`/`CalendarUrlParser`, 스크래치패드) + verifier 정적 대조 | 8,188케이스(월 2,412×2 + 이벤트 55종×60일 + URL 22종) 불일치 0. verifier PASS: diff가 spec 범위에 한정, 의미 동일. |
| 2026-09-21 | T0.2-T3 테스트 작성 | verifier: build, test 전체/필터, `--list-tests` 이름 전수 대조, 금지 API grep, 의미 검토, 인코딩 | PASS. 83건 통과(CalendarGrid 51, NativeBridgeProtocol 11, CalendarUrlParser 15, MainViewModel 5, 상수 가드 1). 2026-09-01=화, 2026-11-01=일 요일 재확인. |
| 2026-09-21 | P0 중간 검토 | reviewer(Fable): 설계 일치·불변 조건·구조·테스트 품질·csproj 설정·P1 제약 | **approve**. blocker/major 없음. minor 5건(아래). |

### P0 중간 검토 minor (P1 이후 이월)

1. `tests/DaouCalendarOverlay.Tests/DaouCalendarOverlay.Tests.csproj` RID/SelfContained/ValidateExecutableReferencesMatchSelfContained 3속성은 메인 csproj 전역 RID 우회 → T2.6에서 메인 csproj 정리와 함께 제거.
2. `MainViewModelTests.cs`의 `RunSta` 헬퍼 미사용 → T1.8 테스트 재작성 시 삭제.
3. `MainViewModelTests.cs`가 `DateTime.Today`를 직접 읽음(자정 경계 flaky 가능) → T1.8에서 고정 시계 주입으로 교체.
4. `CalendarGridTests.OccursOnDate_AllDay_UsesEventOwnOffsetDateNotInstant`는 9/22 false 단언이 없어 종일(이벤트 오프셋 벽시계)/시간(KST 인스턴트) 비대칭을 완전히 고정하지 못함 → 후속 보강 시 추가.
5. `DaouCalendarOverlay.sln`은 `dotnet sln add`가 BOM+CRLF+탭으로 재작성하고 x64/x86 구성을 추가함(무해).

### P1 착수 제약 (중간 검토에서 P1 lead에게 전달)

1. `CalendarGrid`는 시계를 갖지 않는다. T1.8의 `Now` 주입은 `MainViewModel` 생성자 파라미터(`Func<DateTime>` 또는 `TimeProvider`)로 하고 `DateTime.Today` 3곳(`MainViewModel.cs` 생성자·`GoToday`·`BuildCalendar`)을 모두 교체. `CalendarGrid` 시그니처 유지.
2. "오늘"의 기준(로컬 vs KST)을 T1.8에서 결정하고 문서화. KST 고정이면 `CalendarGrid.GetToday(DateTimeOffset now) => now.ToOffset(KstOffset).Date` 권장.
3. T1.3: 스킴/호스트 정책은 `Services/BaseUrlPolicy`에만. `CalendarUrlParser`에 https 검증을 넣지 않는다. `Parse_AcceptsHttpScheme` 유지.
4. T1.4: dedupe는 `SetEvents` 입력 단계의 순수 함수로 분리. `OccursOnDate`·정렬 계약 불변. 숨김 판정 "모두 숨김일 때만"은 `_hiddenCalendarIds.Contains` 2곳에 동일 적용.
5. T1.6: 캐시 `RangeFrom/RangeTo`는 `CalendarGrid.GetVisibleRange(month)` 반환값 그대로 저장. 범위 밖 판정도 `KstOffset` 기준.
6. T2.6: 메인 csproj RID/SelfContained를 pubxml로 옮길 때 테스트 csproj 3속성 함께 제거. 완료 기준에 포함.
7. InternalsVisibleTo 금지 유지. 테스트 필요 시 순수 로직을 public 정적 클래스로 추출.
8. `ProtocolConstantsTests`는 6절 금지 상수 가드. 깨지면 테스트가 아니라 변경을 되돌린다.
9. 모든 P1 태스크 완료 기준에 테스트 포함. STA 헬퍼 없이 순수 로직만 대상.
