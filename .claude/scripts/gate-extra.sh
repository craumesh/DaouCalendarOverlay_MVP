#!/bin/bash
# DaouCalendarOverlay 전용 GATE_ALWAYS (gate.conf에서 호출). 프로젝트 루트에서 실행된다.
# 1) 기술 문서 HTML 형식 점검(태그 균형, pre 안 미이스케이프, id 중복, nav 대상, h2 번호 연속).
# 2) .cs 변경이 없는데 테스트가 읽는 파일(문서, README, CHANGELOG, worker, manifest, csproj, xaml, publish.ps1)이
#    바뀌었으면 빌드·테스트를 돈다. .cs가 바뀌었으면 GATE_CSHARP가 이미 돌았으므로 중복 실행하지 않는다.
set -e

node tools/check-html.js docs/DaouCalendarOverlay_Technical_Documentation.html

changed=$( { git diff --name-only HEAD 2>/dev/null; git ls-files --others --exclude-standard; } | grep -v '^\.claude/logs/' | sort -u)

if printf '%s\n' "$changed" | grep -Eiq '\.cs$'; then
  exit 0
fi

if printf '%s\n' "$changed" | grep -Eiq '\.(html|md|js|json|csproj|sln|xaml|manifest|ps1|props|targets|pubxml)$'; then
  dotnet build DaouCalendarOverlay.sln -c Release -warnaserror -nologo -v q
  dotnet test DaouCalendarOverlay.sln -c Release --no-build -nologo
fi
