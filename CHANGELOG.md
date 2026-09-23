# Changelog

이 파일은 Keep a Changelog 형식을 따르고 버전은 유의적 버전(SemVer)을 따릅니다.
WPF 앱(EXE)과 Chrome 확장은 같은 버전 번호를 씁니다. 네이티브 브리지 프로토콜 버전은 별도로 `NativeBridgeProtocol.ProtocolVersion` 으로 관리합니다(현재 v1, 파이프 이름 `DaouCalendarOverlay.NativeBridge.v7`).

## 7.1.0 - 2026-09-23

### Added
- 파일 로깅(`%LOCALAPPDATA%\DaouCalendarOverlay\logs`, 1MB x 5 롤링)과 트레이 "로그 폴더 열기"
- 설정·캐시 저장의 원자적 쓰기(임시 파일 + 교체)
- 캐시에 마지막 동기화 시각과 조회 범위(`LastUpdated`/`RangeFrom`/`RangeTo`) 기록, 범위 밖 캐시 표기
- `tools/Measure-HostSpawn.ps1`: native host 스폰당 경과 시간·CPU 측정 스크립트(측정 결과는 기술 문서 성능 절)
- Chrome 확장 버전 불일치 감지: `getConfig`의 `extensionVersion`을 EXE 기대 버전(`ChromeExtensionInstaller.ExpectedExtensionVersion`)과 비교해 상태에 "Chrome 확장 새로고침 필요 (x → y)" 표시(`ExtensionVersionGuard`)
- 버전 체계: EXE `Version`/`FileVersion`/`InformationalVersion`(빌드 커밋 SHA 자동 포함), `NativeBridgeProtocol.ProtocolVersion = 1`, 트레이 툴팁·설정창 하단·로그 첫 줄에 버전 표시, 이 CHANGELOG 신설

### Changed
- Chrome 확장 7.0.0 → 7.1.0, 서비스 워커 파일명 `service-worker-v710.js`, 확장이 `extensionVersion`을 앱에 전달
- Native host 경량화: 커스텀 `Program.Main`이 WPF `Application` 생성 전에 host 모드를 분기해, host 스폰마다 `App`·App.xaml 리소스를 만들지 않음(`StartupModeParser`)
- 확장 동기화 트리거 정리: 서비스 워커 최상위 `syncOnce()` 호출 제거, 쿠키 변경 5초 debounce, host 연결 3회 연속 실패 시 알람 주기 1 → 2 → 5분 백오프 후 성공 시 30초 복귀, 알람 주기 불일치 시 재생성
- DaouOffice 주소 검증을 https + `*.daouoffice.com` 정책 한 곳(`BaseUrlPolicy`)으로 통일
- 동기화 상태 문구를 `SyncStatusService` 한 곳에서만 생성
- 자동 새로고침 주기를 1~1440분으로 제한하고 캘린더 ID 입력을 검증

### Fixed
- 여러 캘린더에 공유된 일정을 `id` 기준 1건으로 합치고 소속 캘린더 이름을 모두 표기
- 자정·월 전환 시 오늘 날짜 갱신 누락

### Security
- 네임드 파이프 `CurrentUserOnly` + 서버 프로세스 실행 파일 경로 대조 후에만 쿠키 전달
- 로그에 쿠키 값과 토큰을 남기지 않음(개수/출처만 기록)

> P2(배포·운영 정비) 작업이 진행 중입니다. 이후 P2 작업의 결과는 각 작업에서 이 7.1.0 절에 추가합니다.

## 7.0.0 - 2026-09-18

- 기술 문서 v7.0.0 as-built 기준의 최초 구현: WPF 오버레이, Chrome MV3 확장 + Native Messaging + 네임드 파이프 브리지, 단일 파일 self-contained 배포, 로컬 설정/캐시 저장.
