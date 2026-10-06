# .NET 10 이전 시험 기록

> **상태**: 이 문서는 7.1.0 시점(2026-09-23, 테스트 391건 기준)의 시험 기록이다. .NET 10 전환은 7.3.1에서 했다. 현재 사실은 기술 문서 §24.7과 `docs/verification-log.md`의 7.3.1 절에 있다. 아래 §1~§5는 당시 기록이므로 그대로 두고, §6 체크리스트만 전환 결과에 맞춰 갱신했다.

작업 지시서 T2.7(.NET 10 이전 시험, Q5-b)의 측정 결과다. 측정은 작업 트리에서 두 csproj의 TFM만 임시로 바꿔 수행했고, 측정이 끝난 뒤 TFM은 `net8.0-windows`로 되돌렸다. 이 문서의 수치는 모두 아래 2절의 스크립트가 실제로 출력한 값이다.

## 1. 배경

- 현재 타깃 프레임워크(TFM)는 `net8.0-windows`다(`DaouCalendarOverlay/DaouCalendarOverlay.csproj`, `tests/DaouCalendarOverlay.Tests/DaouCalendarOverlay.Tests.csproj`).
- .NET 8의 지원 종료일은 **2026-11-10**이다. 근거는 작업 지시서 T2.7의 "웹 확인" 기록이며, 이 시험에서 웹으로 다시 확인하지는 않았다.
- 측정 머신의 SDK: `dotnet --version` 출력 `10.0.401`(설치된 SDK는 이것 하나). 설치된 공유 런타임은 `Microsoft.NETCore.App`/`Microsoft.WindowsDesktop.App` 7.0.20, 8.0.31, 10.0.12다(`dotnet --list-runtimes`).
- 사용자 결정 **Q5-b**: .NET 10 전환은 별도 릴리스로 하고, 이번에는 시험(측정과 기록)만 한다. 릴리스 전환 여부와 시점은 사용자가 결정한다.

## 2. 시험 방법

- 실행 스크립트: `tools/net10-trial.ps1`(인자 없이 기본값으로 실행).

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\net10-trial.ps1
  ```

  기본값은 `-BaselineTfm net8.0-windows`, `-TrialTfm net10.0-windows`, `-OutRoot %TEMP%\net10-trial`, `-HostRuns 3`이다. 스크립트는 두 csproj의 `<TargetFramework>`만 `net8.0-windows` → `net10.0-windows` 순서로 바꿔 가며 시나리오마다 아래 항목을 측정하고, 끝나면 finally 블록에서 두 csproj를 원본 바이트로 되돌린 뒤 `dotnet build -c Release`를 한 번 더 돌려 net8 산출물 상태로 복구한다. dotnet CLI 출력 언어는 스크립트 안에서 영어(`DOTNET_CLI_UI_LANGUAGE=en`)로 고정된다.
- 측정 항목 정의
  - **Release 빌드 시간**: `dotnet build DaouCalendarOverlay.sln -c Release --nologo`의 벽시계 시간(암묵 restore 포함). 경고 수는 같은 출력에서 소문자 `warning`을 포함한 줄을 중복 제거해 센 값이다.
  - **publish 시간 / publish EXE 크기**: `publish.ps1`을 거치지 않고 다음 인자를 그대로 넘긴 `dotnet publish`의 벽시계 시간과 산출 `DaouCalendarOverlay.exe` 바이트 수.
    `dotnet publish DaouCalendarOverlay\DaouCalendarOverlay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o %TEMP%\net10-trial\<tfm>`
  - **dotnet test 결과**: `dotnet test DaouCalendarOverlay.sln -c Release --nologo --filter "FullyQualifiedName!~TargetFrameworkGuardTests"`의 종료 코드·시간·통과 수.
  - **host 모드 왕복**: 게시 EXE를 `chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/` 인자로 실행해 stdin에 프레임 1개(4바이트 LE 길이 + UTF-8 `{"type":"getConfig"}`)를 쓰고 닫은 뒤, stdout을 EOF까지 읽고 프로세스가 종료할 때까지의 경과 시간. 3회 실행해 중앙값을 낸다.
- **TFM 가드 테스트 제외**: `dotnet test`는 두 시나리오 모두 `--filter "FullyQualifiedName!~TargetFrameworkGuardTests"`로 `TargetFrameworkGuardTests`를 제외하고 실행했다. 이 테스트 클래스는 앱·테스트 어셈블리가 `.NETCoreApp,Version=v8.0`을 겨냥하는지 단언하는 TFM 원복 가드라서 net10 시나리오에서는 설계상 반드시 실패한다. 제외하지 않으면 net10 열의 테스트 결과가 항상 "실패"로 기록돼 비교가 왜곡되므로, 같은 필터를 두 시나리오에 똑같이 붙였다. 필터 없는 일반 `dotnet test`에서는 이 가드가 항상 실행된다.
- 측정 일시·머신: 2026-09-23 12:51:55 ~ 12:52:25 (+09:00), 머신 `DESKTOP-50OSUI3`, Windows 11 Pro 10.0.26200, 기준 커밋 `24c9f41`(T2.6) + T2.7 작업 트리. 게시 EXE의 `InformationalVersion`은 두 시나리오 모두 `7.1.0+24c9f416c9c47587f7ca058911614e4cbeaf2c8b`.
- **host 왕복의 전제(계획과 다름)**: 계획은 "오버레이 GUI 미실행 → 파이프 연결 2,500ms 타임아웃이 양쪽에 동일 포함"이었으나, 측정 당시 저장소 밖의 다른 사본(`C:\Users\USER\Desktop\DaouCalendarOverlay_MVP\...\bin\Release\net8.0-windows\win-x64\DaouCalendarOverlay.exe`, PID 13624)이 오버레이로 실행 중이었다. 파이프 이름이 같아서 host는 이 서버에 바로 연결됐고, 서버 EXE 경로가 자기 경로와 달라 `PipePeerVerifier`가 요청 전송을 거부(fail-closed)했다. 따라서 이번 host 왕복 수치에는 **2,500ms 파이프 타임아웃이 포함되지 않았다**. 대신 "프로세스 기동 → 프레임 수신 → 파이프 연결 → 서버 신원 확인 → 오류 프레임 응답 → 종료" 경로가 두 시나리오에 동일하게 포함된다(두 시나리오 모두 같은 236바이트 오류 응답, 3절 참고). 스크립트 비고 칸의 "오버레이 GUI 미실행 시 파이프 타임아웃 약 2.5초 포함"은 조건문이며 이번 측정에는 해당하지 않는다. 측정 중 다른 빌드는 병행하지 않았다.
- 캐시 상태: NuGet 캐시에 런타임 팩 `Microsoft.NETCore.App.Runtime.win-x64`/`Microsoft.WindowsDesktop.App.Runtime.win-x64` 8.0.31과 10.0.12가 이미 있어 publish 중 다운로드는 없었다(publish에 쓰인 버전은 각 `deps.json` 기준 8.0.31, 10.0.12). 또 `obj/Release/net10.0-windows`와 `bin/Release/net10.0-windows`가 이 측정 7분 전(12:44:53)에 이미 만들어져 있었고, net8 산출물도 있었으므로 빌드 시간은 두 시나리오 모두 **증분 빌드** 값이다. 깨끗한 상태(첫 실행, NuGet 복원 포함)의 빌드 시간은 측정하지 않았다.

## 3. 비교표

`%TEMP%\net10-trial\result.md`의 값을 그대로 옮겼다. MB는 1 MB = 1,048,576 bytes 기준이다.

| 항목 | net8.0-windows | net10.0-windows | 비고 |
|---|---|---|---|
| Release 빌드 시간 | 1,156 ms | 1,364 ms | 벽시계, restore 포함. 두 시나리오 모두 이전 산출물(obj)이 있는 증분 빌드 |
| 빌드 경고 수 | 0 | 0 | warning 포함 줄, 중복 제거 |
| publish 시간 | 6,391 ms | 6,750 ms | win-x64 self-contained single-file |
| publish EXE 크기 | 72,087,080 bytes (68.75 MB) | 75,884,439 bytes (72.37 MB) | net10이 3,797,359 bytes(3.62 MB, 약 +5.3%) 큼 |
| dotnet test 결과 | 성공 (exit 0, 6,005 ms), 통과 391 / 실패 0 / 건너뜀 0 / 전체 391 | 성공 (exit 0, 5,389 ms), 통과 391 / 실패 0 / 건너뜀 0 / 전체 391 | TFM 가드 테스트 제외 |
| host 모드 왕복(중앙값) | 292 ms (각 회: 716 ms / 279 ms / 292 ms) | 304 ms (각 회: 619 ms / 304 ms / 288 ms) | 이번 측정은 다른 오버레이 사본이 실행 중이라 파이프 연결 즉시 서버 신원 확인에서 거부됨. 2,500ms 타임아웃 미포함(두 시나리오 동일) |

보충 관찰(스크립트 표 밖의 값, 출처 명시):

- host 응답: 6회 모두 exit 0, stdout 236 bytes. 응답 본문은 두 시나리오가 같다: `{"ok":false,"error":"Named Pipe 서버(PID 13624)가 Daou Calendar Overlay 실행 파일이 아니어서 요청을 전송하지 않았습니다.","config":null}`.
- `%LOCALAPPDATA%\DaouCalendarOverlay\logs\host-20260923.log`에 6회 모두 `mode=host` 시작 줄과 `요청 완료 elapsed=…ms`가 남았다. host 내부 처리 시간(요청 수신 → 응답 완료)은 net8 40 / 190 / 204 ms, net10 41 / 222 / 208 ms였다. 같은 시각 `overlay-*.log`에는 기록이 없어, 두 TFM 모두 host 모드에서 WPF `App`을 만들지 않았다.
- 원복 후 Release 빌드(net8): 성공(exit 0, 1,364 ms). 스크립트 종료 코드 0.

## 4. 관찰된 문제

- **net10 빌드·publish·테스트의 경고/오류: 없음.** 솔루션 빌드(앱 + 테스트 프로젝트) 경고 0, publish exit 0, 테스트 391건 모두 통과. 소스 코드 변경 없이 TFM만 바꿔 통과했다.
  - WPF/WinForms 관련 경고: 없음(`UseWPF`와 `UseWindowsForms`를 함께 쓰는 현재 구성 그대로).
  - publish 단계 경고: 스크립트는 publish 경고를 따로 세지 않으므로, 측정 후 앱 csproj의 TFM만 임시로 `net10.0-windows`로 바꿔 같은 인자로 한 번 더 publish해 확인했다. 콘솔 요약 `0 Warning(s)`, `0 Error(s)`, 산출 EXE 75,884,439 bytes(스크립트 값과 동일)였고, 확인 직후 csproj를 원본 바이트로 되돌렸다.
  - 테스트 SDK 호환: `Microsoft.NET.Test.Sdk` 17.12.0, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2가 net10 테스트 프로젝트에서 그대로 동작했다(패키지 버전 변경 불필요).
- **PublishSingleFile 압축 차이**: 두 시나리오 모두 `EnableCompressionInSingleFile=true`로 같은 설정이지만 net10 EXE가 3.62 MB(약 5.3%) 크다. 원인은 분석하지 않았다(런타임 팩 자체의 크기 차이로 추정).
- **host 모드 동작 차이: 없음.** 응답 바이트·본문·종료 코드가 같고, 왕복 중앙값 차이(+12 ms)는 첫 회를 제외한 각 회 값의 범위(279~304 ms) 안에 있다. 두 시나리오 모두 첫 회만 느렸는데(716 ms, 619 ms) 원인은 분리하지 않았다(새로 쓴 EXE의 첫 실행 비용으로 추정).
- **GUI 기동 확인은 부분 확인만 했다.** net10 게시 EXE를 인자 없이 실행하면 exit 0으로 끝났고 `overlay-20260923.log`에 `DaouCalendarOverlay 7.1.0 (7.1.0+24c9f416c9c47587f7ca058911614e4cbeaf2c8b) mode=overlay pid=13600`이 기록됐다. 즉 net10에서 WPF `App` 생성, App.xaml 로드, `OnStartup` 진입까지는 동작했다. 다만 다른 오버레이 사본이 단일 인스턴스 뮤텍스를 잡고 있어 이 프로세스는 기존 인스턴스에 활성화를 넘기고 종료했으므로, **net10에서 오버레이 창 표시·Native Messaging 등록·브리지 서버 동작은 확인하지 못했다.**
- 측정 조건상의 한계(문제는 아니지만 수치 해석에 필요): 빌드 시간은 증분 빌드 값이고, host 왕복에는 파이프 타임아웃이 빠져 있다(2절). framework-dependent 개발 빌드 EXE를 net10으로 실행하는 경우와, Chrome 확장과의 실제 동기화는 시험하지 않았다.

## 5. 결론과 권고

- 기술적 장벽은 발견되지 않았다. 소스 변경 없이 TFM만 `net10.0-windows`로 바꿔 빌드(경고 0)·publish·테스트(391건 통과)·host 모드 왕복이 모두 성공했다.
- 측정된 비용은 작다: 게시 EXE +3.62 MB(+5.3%), publish +359 ms, host 왕복 중앙값 +12 ms(첫 회를 제외한 각 회 값의 범위 279~304 ms 안). 증분 빌드 시간 차이(+208 ms)도 작다.
- 전환 이득은 성능이 아니라 지원 기간이다. .NET 8은 2026-11-10에 지원이 끝나 이후 보안 패치를 받지 못하므로, 이 날짜 전에 별도 릴리스로 전환하는 것을 권고한다.
- 전환 릴리스 전에 추가로 확인할 것: (1) 다른 오버레이를 모두 종료한 상태에서 net10 EXE의 창 표시·Native Messaging 등록·Chrome 동기화 왕복, (2) 파이프 타임아웃이 포함되는 조건(오버레이 미실행)의 host 왕복, (3) 깨끗한 상태의 빌드 시간.
- **릴리스 전환은 사용자 결정(Q5-b)**이다. 이번 작업에서 현재 브랜치의 TFM은 `net8.0-windows`로 유지했다(두 csproj 모두 원복 확인, `TargetFrameworkGuardTests`가 이를 고정).

## 6. 전환 체크리스트

실제로 전환할 때 함께 바꾸거나 확인할 지점:

- [x] (a) 두 csproj의 `<TargetFramework>`: `DaouCalendarOverlay/DaouCalendarOverlay.csproj`, `tests/DaouCalendarOverlay.Tests/DaouCalendarOverlay.Tests.csproj`를 `net10.0-windows`로(테스트 프로젝트가 앱 프로젝트를 참조하므로 둘을 함께 바꿔야 빌드된다). (완료: T1, 커밋 `b1e5337`)
- [x] (b) `tests/DaouCalendarOverlay.Tests/TargetFrameworkGuardTests.cs`의 기대 문자열 `.NETCoreApp,Version=v8.0` → `.NETCoreApp,Version=v10.0`(메서드 이름 `*_TargetsNet8Windows`도 함께). (완료: T1, 커밋 `b1e5337`)
- [x] (c) README `## 요구 사항`의 "`.NET 8 SDK`" 표기. 같은 README의 `## 기술 스택` ".NET 8"과 `## 빌드`의 출력 폴더 `DaouCalendarOverlay\bin\Release\net8.0-windows\`도 함께. (완료: T5, 커밋 `07fba21`)
- [x] (d) 기술 문서 §16 빌드·배포 구조의 csproj 발췌(`<TargetFramework>net8.0-windows</TargetFramework>`). 같은 문서의 §4 기술 스택, §24 개발 빌드 출력 경로, §19의 ".NET 8 지원 종료" 항목도 함께. (완료: T6·T7, 커밋 `e459030`·`253074c`. §24.7과 §34·§38은 T8 `0d306a0`)
- [x] (e) `publish.ps1` 인자 확인: 현재 `publish.ps1`과 `Properties/PublishProfiles/win-x64.pubxml`에는 TFM이 없다(csproj 한 곳에서만 관리). 전환 후 `publish.ps1 -DryRun`과 실제 publish로 산출물명(`DaouCalendarOverlay-<버전>.exe`)·`publish\symbols\` PDB 분리가 그대로인지 확인한다. (완료: `docs/verification-log.md` 7.3.1 자동 검증의 P0·P1, 커밋 `2f14465`)
- [x] (f) 자동 시작 EXE 교체 시 재게시 필요: 설치된 EXE는 새로 게시한 net10 EXE로 교체해야 한다(설치·업그레이드 절차: EXE 종료 → 교체 → 실행 → `chrome://extensions` 새로고침). Native Messaging manifest의 `path`는 오버레이 기동 때마다 현재 EXE 경로로 갱신되므로, 개발 빌드 경로가 `bin\Release\net8.0-windows\`에서 `bin\Release\net10.0-windows\`로 바뀌면 새 경로의 EXE로 오버레이를 한 번 실행해 등록을 갱신한다(갱신 전에는 Chrome이 옛 EXE를 띄우고, 서버 EXE 경로가 달라 동기화가 거부된다). (완료: `docs/verification-log.md` 수동 확인 대기 (7.3.1)의 M1·M3·M4, 2026-10-06 이 PC. 7.3.1 EXE를 `%LOCALAPPDATA%\Programs\DaouCalendarOverlay-7.3.1\`에 두고 실행한 09:27:07 `startup` 줄 뒤 Run 값과 Native Messaging manifest의 `path`가 그 EXE를 가리켰고, 확장 새로고침 뒤 동기화가 `verdict=success`로 이어졌다. 5절의 전환 전 확인 (2) 오버레이 미실행 host 왕복도 같은 절의 H2-b에서 확인했다: `ok:false`, 경과 2647.7 ms, host 로그 `요청 완료 elapsed=2541ms`)
- [x] (g) `tools/Measure-HostSpawn.ps1` 도움말의 예시 경로 `bin\Release\net8.0-windows\`, `tools/net10-trial.ps1`의 기본값(`-BaselineTfm net8.0-windows`)과 가드 테스트 제외 필터. (완료: T9. 예시 경로와 런타임 표기를 고쳤고, `net10-trial.ps1`은 코드를 바꾸지 않고 주석에 중단 조건과 `-BaselineTfm net10.0-windows -TrialTfm <비교 TFM>` 사용법을 더했다)
- [x] (h) framework-dependent 개발 빌드를 실행하는 머신에는 .NET 10 Desktop Runtime이 필요하다(self-contained 릴리스 EXE는 불필요). (완료: README와 기술 문서 §23·§24.1, 커밋 `07fba21`·`253074c`)
- [x] (i) `CHANGELOG.md`에 전환 릴리스 항목 추가. (완료: `CHANGELOG.md` 7.3.1 항목, 커밋 `9defa88`)
