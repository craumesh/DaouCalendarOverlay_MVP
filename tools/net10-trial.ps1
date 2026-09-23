param([string]$TrialTfm = 'net10.0-windows', [string]$BaselineTfm = 'net8.0-windows', [string]$OutRoot = (Join-Path $env:TEMP 'net10-trial'), [int]$HostRuns = 3, [switch]$SkipHostProbe)
$ErrorActionPreference = 'Stop'

# T2.7 .NET 10 이전 시험 비교 측정 스크립트.
#
# 사용법(어느 디렉터리에서 실행해도 된다):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\net10-trial.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\net10-trial.ps1 -HostRuns 5
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\net10-trial.ps1 -SkipHostProbe
#
# 하는 일:
#   1. 두 csproj(앱, 테스트)의 <TargetFramework>만 $BaselineTfm, $TrialTfm 순서로 바꿔 가며 시나리오마다
#      Release 빌드, self-contained single-file publish, dotnet test, host 모드 stdio 왕복을 측정한다.
#      각 항목은 실패해도 다음 항목으로 넘어가고, 실패 항목에는 종료 코드와 출력 마지막 20줄을 남긴다.
#   2. 두 csproj의 원본 텍스트를 메모리에 담아 두고 try/finally의 finally에서 항상 되돌린다(예외, Ctrl+C 포함).
#      finally조차 돌지 못하는 강제 종료에 대비한 원본 사본은 $OutRoot\csproj-backup 에만 둔다.
#      저장소 안에는 백업 파일을 만들지 않는다.
#   3. 결과를 Markdown 표로 콘솔에 출력하고 $OutRoot\result.md 에도 저장한다.
#   4. 마지막에 Release 빌드를 한 번 더 돌려 bin/obj를 원래 TFM 산출물 상태로 돌려 놓는다.
#
# 하지 않는 일:
#   - publish.ps1을 호출하지 않는다. publish 인자는 Measure-Publish가 직접 넘긴다
#     (publish.ps1이나 publish 프로파일이 바뀌어도 이 스크립트는 그대로 동작한다).
#   - 브랜치 생성, 커밋 같은 저장소 이력 작업을 하지 않는다. 측정이 끝나면 두 csproj는 원래 내용 그대로다.
#
# 종료 코드: 모든 측정 항목, csproj 원복, 마지막 Release 빌드가 성공하면 0, 하나라도 실패하면 1.

$repo = Split-Path -Parent $PSScriptRoot
$solution = "$repo\DaouCalendarOverlay.sln"
$mainProject = "$repo\DaouCalendarOverlay\DaouCalendarOverlay.csproj"
$testProject = "$repo\tests\DaouCalendarOverlay.Tests\DaouCalendarOverlay.Tests.csproj"
$projectFiles = @($mainProject, $testProject)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$tailLineCount = 20

# TargetFrameworkGuardTests는 두 어셈블리가 .NETCoreApp v8.0을 겨냥하는지 단언하므로 net10 시나리오에서 설계상 실패한다.
# 그대로 두면 net10 열의 테스트 결과가 항상 실패가 되어 비교가 왜곡되므로, 이 스크립트의 dotnet test에서만
# 두 시나리오에 똑같이 제외한다. 필터 없는 일반 dotnet test에서는 항상 실행된다.
# PowerShell이 '!'를 해석하지 않도록 작은따옴표로 감싼다.
$testFilter = 'FullyQualifiedName!~TargetFrameworkGuardTests'
$testNote = 'TFM 가드 테스트 제외'

# Chrome이 native messaging host를 띄울 때 넘기는 확장 origin(NativeBridgeProtocol.ExtensionOrigin).
$hostOrigin = 'chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/'
# 오버레이 GUI가 실행 중이 아니면 host는 파이프 연결을 약 2,500ms 기다린 뒤 오류 프레임을 돌려주고 종료한다.
# 이 파이프 타임아웃은 두 시나리오에 똑같이 포함되므로 비교에는 지장이 없다.
$hostNote = '오버레이 GUI 미실행 시 파이프 타임아웃 약 2.5초 포함(두 시나리오 동일)'
# host가 응답하지 않고 멈춰도 측정 전체가 멈추지 않게 하는 안전장치.
$hostTimeoutMs = 60000

function Format-Number([double]$Value, [string]$Format) {
    return $Value.ToString($Format, $invariant)
}

function Format-Ms($Milliseconds) {
    return (Format-Number ([Math]::Round([double]$Milliseconds)) 'N0') + ' ms'
}

function Format-Cell([string]$Text) {
    if ($null -eq $Text) { return '' }
    return (($Text -replace '\r?\n', ' ') -replace '\|', '\|')
}

function Get-Tail([string[]]$Lines) {
    if ($null -eq $Lines) { return @() }
    return @($Lines | Select-Object -Last $tailLineCount)
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $count = $sorted.Count
    if ($count -eq 0) { return $null }
    $mid = [int][Math]::Floor($count / 2)
    if ($count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Get-Tfm([string]$path) {
    $match = [regex]::Match([System.IO.File]::ReadAllText($path), '<TargetFramework>([^<]*)</TargetFramework>')
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}

function Set-Tfm([string]$path, [string]$tfm) {
    $text = [System.IO.File]::ReadAllText($path)
    $regex = [regex]'<TargetFramework>[^<]*</TargetFramework>'
    if (-not $regex.IsMatch($text)) { throw "<TargetFramework> 요소를 찾지 못했습니다: $path" }
    # 첫 번째 <TargetFramework> 한 곳만 바꾼다. 버전 속성 등 다른 내용은 그대로 둔다.
    $updated = $regex.Replace($text, "<TargetFramework>$tfm</TargetFramework>", 1)
    [System.IO.File]::WriteAllText($path, $updated, (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-Dotnet([string[]]$Arguments, [switch]$Quiet) {
    # 네이티브 명령이 stderr에 쓴 줄은 2>&1 로 합칠 때 ErrorRecord가 된다. Windows PowerShell은
    # $ErrorActionPreference = 'Stop' 이면 그 순간 예외를 던지므로 이 함수 범위에서만 Continue로 둔다.
    # 실패 판정은 종료 코드($LASTEXITCODE)로 한다.
    $ErrorActionPreference = 'Continue'
    if (-not $Quiet) { Write-Host ('  > dotnet {0}' -f ($Arguments -join ' ')) }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    $sw.Stop()
    if (-not $Quiet) { Write-Host ('    exit {0}, {1}' -f $exitCode, (Format-Ms $sw.Elapsed.TotalMilliseconds)) }
    return [pscustomobject]@{
        ExitCode  = $exitCode
        ElapsedMs = $sw.Elapsed.TotalMilliseconds
        Lines     = $lines
    }
}

function Get-WarningCount([string[]]$Lines) {
    # MSBuild 콘솔 로거는 같은 경고를 진행 중에 한 번, 끝의 요약에서 한 번 더 출력하므로 같은 줄은 한 번만 센다.
    # 요약의 "N Warning(s)" 줄은 대문자 W라 대소문자 구분 비교(-cmatch)에 걸리지 않는다.
    $matched = @($Lines |
        Where-Object { $_ -cmatch '\bwarning\b' -and $_ -notmatch 'warning\(s\)' } |
        ForEach-Object { $_.Trim() } |
        Sort-Object -Unique)
    return $matched.Count
}

function Get-TestSummary([string[]]$Lines) {
    # vstest 요약 줄. 영문 "Failed: 0, Passed: N, Skipped: 0, Total: N", 한국어 "실패: 0, 통과: N, 건너뜀: 0, 전체: N".
    $pattern = '(?:Failed|실패)\s*:\s*(\d+),\s*(?:Passed|통과)\s*:\s*(\d+),\s*(?:Skipped|건너뜀)\s*:\s*(\d+),\s*(?:Total|전체)\s*:\s*(\d+)'
    $found = $false
    $failed = 0; $passed = 0; $skipped = 0; $total = 0
    foreach ($line in $Lines) {
        $m = [regex]::Match($line, $pattern)
        if ($m.Success) {
            $found = $true
            $failed += [int]$m.Groups[1].Value
            $passed += [int]$m.Groups[2].Value
            $skipped += [int]$m.Groups[3].Value
            $total += [int]$m.Groups[4].Value
        }
    }
    if (-not $found) { return $null }
    return ('통과 {0} / 실패 {1} / 건너뜀 {2} / 전체 {3}' -f $passed, $failed, $skipped, $total)
}

function New-StepResult([string]$Name) {
    return [pscustomobject]@{
        Name      = $Name
        Ran       = $false
        Success   = $false
        Skipped   = $false
        ExitCode  = $null
        ElapsedMs = $null
        Warnings  = $null
        ExeBytes  = $null
        Summary   = $null
        Samples   = @()
        MedianMs  = $null
        Tail      = @()
    }
}

function Measure-Build {
    $step = New-StepResult 'dotnet build'
    try {
        $run = Invoke-Dotnet @('build', $solution, '-c', 'Release', '--nologo')
        $step.Ran = $true
        $step.ExitCode = $run.ExitCode
        $step.ElapsedMs = $run.ElapsedMs
        $step.Warnings = Get-WarningCount $run.Lines
        $step.Success = ($run.ExitCode -eq 0)
        if (-not $step.Success) { $step.Tail = @(Get-Tail $run.Lines) }
    }
    catch {
        $step.Tail = @("예외: $($_.Exception.Message)")
    }
    return $step
}

function Measure-Publish([string]$Tfm) {
    $step = New-StepResult 'dotnet publish'
    $publishDir = "$OutRoot\$Tfm"
    try {
        if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
        # publish.ps1을 거치지 않고 릴리스 게시 설정을 인자로 직접 넘긴다.
        $run = Invoke-Dotnet @('publish', $mainProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
            '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publishDir)
        $step.Ran = $true
        $step.ExitCode = $run.ExitCode
        $step.ElapsedMs = $run.ElapsedMs
        $exe = Join-Path $publishDir 'DaouCalendarOverlay.exe'
        if (Test-Path -LiteralPath $exe -PathType Leaf) {
            $step.ExeBytes = (Get-Item -LiteralPath $exe).Length
            Write-Host ('    EXE {0} bytes' -f (Format-Number $step.ExeBytes 'N0'))
        }
        $step.Success = ($run.ExitCode -eq 0) -and ($null -ne $step.ExeBytes)
        if (-not $step.Success) {
            $tail = @(Get-Tail $run.Lines)
            if ($null -eq $step.ExeBytes) { $tail += "게시 EXE 없음: $exe" }
            $step.Tail = $tail
        }
    }
    catch {
        $step.Tail = @("예외: $($_.Exception.Message)")
    }
    return $step
}

function Measure-Test {
    $step = New-StepResult 'dotnet test'
    try {
        # 두 시나리오에 같은 필터를 붙인다(TFM 가드 테스트 제외, 위 $testFilter 설명 참고).
        $run = Invoke-Dotnet @('test', $solution, '-c', 'Release', '--nologo', '--filter', $testFilter)
        $step.Ran = $true
        $step.ExitCode = $run.ExitCode
        $step.ElapsedMs = $run.ElapsedMs
        $step.Summary = Get-TestSummary $run.Lines
        $step.Success = ($run.ExitCode -eq 0)
        if ($step.Summary) { Write-Host "    $($step.Summary)" }
        if (-not $step.Success) { $step.Tail = @(Get-Tail $run.Lines) }
    }
    catch {
        $step.Tail = @("예외: $($_.Exception.Message)")
    }
    return $step
}

function Start-HostProcess([System.Diagnostics.ProcessStartInfo]$StartInfo) {
    # Windows PowerShell(.NET Framework)의 Process는 Console.InputEncoding으로 stdin StreamWriter를 만들고,
    # 그 인코딩에 BOM(preamble)이 있으면(예: 콘솔을 UTF-8로 바꾼 경우) 프레임 앞에 BOM 3바이트를 먼저 써 버린다.
    # 그 경우에만 프로세스를 띄우는 동안 BOM 없는 UTF-8로 잠시 바꿨다가 되돌린다.
    $saved = $null
    try {
        if ([Console]::InputEncoding.GetPreamble().Length -gt 0) {
            $saved = [Console]::InputEncoding
            [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
        }
    }
    catch {
        $saved = $null
    }
    try {
        return [System.Diagnostics.Process]::Start($StartInfo)
    }
    finally {
        if ($null -ne $saved) {
            try { [Console]::InputEncoding = $saved } catch { }
        }
    }
}

function Measure-HostRoundTrip([string]$ExePath) {
    # 게시 EXE를 Chrome과 같은 origin 인자로 띄워 stdin에 프레임 1개(4바이트 LE 길이 + UTF-8 JSON)를 쓰고,
    # stdout을 끝까지 읽은 뒤 프로세스가 끝날 때까지의 경과 시간을 잰다.
    # 오버레이 GUI가 실행 중이 아니면 host가 파이프 연결을 약 2,500ms 기다린 뒤 오류 프레임을 돌려주므로
    # 이 시간이 두 시나리오에 똑같이 포함된다.
    $step = New-StepResult 'host 모드 왕복'
    if ($SkipHostProbe) {
        $step.Skipped = $true
        $step.Success = $true
        Write-Host '  host 모드 왕복: 건너뜀(-SkipHostProbe)'
        return $step
    }
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
        $step.Tail = @("게시 EXE 없음: $ExePath")
        Write-Host "  host 모드 왕복: 게시 EXE 없음($ExePath)"
        return $step
    }

    $step.Ran = $true
    Write-Host ('  host 모드 왕복 {0}회: {1} {2}' -f $HostRuns, $ExePath, $hostOrigin)
    Write-Host "    ($hostNote)"

    $payload = [System.Text.Encoding]::UTF8.GetBytes('{"type":"getConfig"}')
    $n = $payload.Length
    $prefix = [byte[]]@(($n -band 0xFF), (($n -shr 8) -band 0xFF), (($n -shr 16) -band 0xFF), (($n -shr 24) -band 0xFF))
    $samples = New-Object System.Collections.Generic.List[object]
    $tail = New-Object System.Collections.Generic.List[string]
    $allOk = $true

    for ($i = 1; $i -le $HostRuns; $i++) {
        $sample = [pscustomobject]@{ Run = $i; ElapsedMs = $null; Bytes = 0; ExitCode = $null; Error = $null; Response = $null }
        $proc = $null
        $buffer = New-Object System.IO.MemoryStream
        try {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $ExePath
            $psi.Arguments = $hostOrigin
            $psi.UseShellExecute = $false
            $psi.RedirectStandardInput = $true
            $psi.RedirectStandardOutput = $true

            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $proc = Start-HostProcess $psi
            $stdin = $proc.StandardInput.BaseStream
            $stdin.Write($prefix, 0, $prefix.Length)
            $stdin.Write($payload, 0, $payload.Length)
            $stdin.Flush()
            $proc.StandardInput.Close()

            # stdout을 EOF까지 MemoryStream으로 읽는다. host가 멈춰도 무한 대기하지 않도록 비동기 복사에 제한 시간을 둔다.
            $copy = $proc.StandardOutput.BaseStream.CopyToAsync($buffer)
            if (-not $copy.Wait($hostTimeoutMs)) {
                try { $proc.Kill() } catch { }
                throw "stdout 읽기가 $hostTimeoutMs ms 안에 끝나지 않았습니다."
            }
            if (-not $proc.WaitForExit($hostTimeoutMs)) {
                try { $proc.Kill() } catch { }
                throw "host 프로세스가 $hostTimeoutMs ms 안에 끝나지 않았습니다."
            }
            $sw.Stop()

            $sample.ElapsedMs = $sw.Elapsed.TotalMilliseconds
            $sample.ExitCode = $proc.ExitCode
            $bytes = $buffer.ToArray()
            $sample.Bytes = $bytes.Length
            if ($bytes.Length -ge 4) {
                $declared = [int]$bytes[0] -bor ([int]$bytes[1] -shl 8) -bor ([int]$bytes[2] -shl 16) -bor ([int]$bytes[3] -shl 24)
                $available = [Math]::Min([Math]::Max($declared, 0), $bytes.Length - 4)
                $text = [System.Text.Encoding]::UTF8.GetString($bytes, 4, $available)
                # 응답 JSON의 \uXXXX 이스케이프(한글 오류 문구)를 읽을 수 있게 풀어 둔다. 표시용이며 판정에는 쓰지 않는다.
                $text = [regex]::Replace($text, '\\u([0-9A-Fa-f]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
                if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + '...' }
                $sample.Response = $text
            }
        }
        catch {
            $sample.Error = $_.Exception.Message
        }
        finally {
            if ($null -ne $proc) { $proc.Dispose() }
            $buffer.Dispose()
        }

        if ($null -ne $sample.Error) {
            $allOk = $false
            $tail.Add(('#{0}: 실패 - {1}' -f $i, $sample.Error))
            Write-Host ('    #{0}: 실패 - {1}' -f $i, $sample.Error)
        }
        else {
            $responseText = if ($sample.Bytes -eq 0) { '응답 없음' } else { 'stdout {0} bytes' -f $sample.Bytes }
            Write-Host ('    #{0}: {1}, {2}, exit {3}' -f $i, (Format-Ms $sample.ElapsedMs), $responseText, $sample.ExitCode)
            if ($sample.Bytes -eq 0) {
                $allOk = $false
                $tail.Add(('#{0}: 응답 없음(stdout 0 bytes), exit {1}' -f $i, $sample.ExitCode))
            }
            elseif ($sample.ExitCode -ne 0) {
                $allOk = $false
                $tail.Add(('#{0}: exit {1}' -f $i, $sample.ExitCode))
            }
        }
        $samples.Add($sample)
    }

    $timed = @($samples | Where-Object { $null -ne $_.ElapsedMs } | ForEach-Object { [double]$_.ElapsedMs })
    if ($timed.Count -gt 0) { $step.MedianMs = Get-Median $timed }
    $firstResponse = @($samples | Where-Object { $_.Response } | Select-Object -First 1)
    if ($firstResponse.Count -gt 0) { $step.Summary = $firstResponse[0].Response }
    $step.Samples = $samples.ToArray()
    $step.Tail = $tail.ToArray()
    $step.Success = $allOk
    if ($null -ne $step.MedianMs) { Write-Host ('    중앙값 {0}' -f (Format-Ms $step.MedianMs)) }
    return $step
}

function Get-NotRunCell($Step) {
    if ($null -eq $Step) { return '측정 안 됨' }
    $tail = @($Step.Tail)
    if ($tail.Count -gt 0) { return '실패 (' + $tail[0] + ')' }
    return '측정 안 됨'
}

function Format-TimeCell($Step) {
    if ($null -eq $Step -or -not $Step.Ran) { return Get-NotRunCell $Step }
    if ($Step.Success) { return Format-Ms $Step.ElapsedMs }
    return ('실패 (exit {0}, {1})' -f $Step.ExitCode, (Format-Ms $Step.ElapsedMs))
}

function Format-WarningCell($Step) {
    if ($null -eq $Step -or -not $Step.Ran) { return Get-NotRunCell $Step }
    $text = [string]$Step.Warnings
    if (-not $Step.Success) { $text += ' (빌드 실패)' }
    return $text
}

function Format-ExeSizeCell($Step) {
    if ($null -eq $Step -or -not $Step.Ran) { return Get-NotRunCell $Step }
    if ($null -eq $Step.ExeBytes) { return ('실패 (exit {0}, 게시 EXE 없음)' -f $Step.ExitCode) }
    return ('{0} bytes ({1} MB)' -f (Format-Number $Step.ExeBytes 'N0'), (Format-Number ($Step.ExeBytes / 1MB) 'N2'))
}

function Format-TestCell($Step) {
    if ($null -eq $Step -or -not $Step.Ran) { return Get-NotRunCell $Step }
    $head = if ($Step.Success) { '성공' } else { '실패' }
    $text = '{0} (exit {1}, {2})' -f $head, $Step.ExitCode, (Format-Ms $Step.ElapsedMs)
    if ($Step.Summary) { $text += ', ' + $Step.Summary }
    return $text
}

function Format-HostCell($Step) {
    if ($null -eq $Step) { return '측정 안 됨' }
    if ($Step.Skipped) { return '건너뜀 (-SkipHostProbe)' }
    if (-not $Step.Ran) { return Get-NotRunCell $Step }
    $parts = @()
    foreach ($sample in $Step.Samples) {
        if ($null -eq $sample.ElapsedMs) { $parts += '실패'; continue }
        $part = Format-Ms $sample.ElapsedMs
        if ($sample.Bytes -eq 0) { $part += ' 응답 없음' }
        if ($null -ne $sample.ExitCode -and $sample.ExitCode -ne 0) { $part += " exit $($sample.ExitCode)" }
        $parts += $part
    }
    $median = if ($null -ne $Step.MedianMs) { Format-Ms $Step.MedianMs } else { '측정 불가' }
    $text = '{0} (각 회: {1})' -f $median, ($parts -join ' / ')
    if (-not $Step.Success) { $text = '실패 · ' + $text }
    return $text
}

function New-Row([string]$Label, [string]$BaselineCell, [string]$TrialCell, [string]$Note) {
    return ('| {0} | {1} | {2} | {3} |' -f (Format-Cell $Label), (Format-Cell $BaselineCell), (Format-Cell $TrialCell), (Format-Cell $Note))
}

# ---- 사전 점검 -------------------------------------------------------------------------------

foreach ($tfm in @($BaselineTfm, $TrialTfm)) {
    if ($tfm -notmatch '^[A-Za-z0-9][A-Za-z0-9.\-]*$') { throw "TFM 형식이 올바르지 않습니다: '$tfm'" }
}
if (-not $SkipHostProbe -and $HostRuns -lt 1) { throw "-HostRuns는 1 이상이어야 합니다: $HostRuns" }
foreach ($path in @($solution) + $projectFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "파일이 없습니다: $path" }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'dotnet CLI를 찾지 못했습니다.' }

$OutRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutRoot)
$repoPrefix = [System.IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
if (($OutRoot.TrimEnd('\') + '\').StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "-OutRoot는 저장소 밖이어야 합니다(백업과 게시 산출물을 저장소에 남기지 않는다): $OutRoot"
}

# 이전 실행이 강제 종료돼 csproj가 시험 TFM으로 남아 있으면 그 상태를 "원본"으로 잡게 되므로 먼저 멈춘다.
foreach ($p in $projectFiles) {
    $current = Get-Tfm $p
    if ($current -ne $BaselineTfm) {
        throw ("{0} 의 현재 TargetFramework가 '{1}'입니다(기대: '{2}'). 이전 측정이 중단됐다면 {3} 의 원본 사본으로 되돌린 뒤 다시 실행하세요." -f $p, $current, $BaselineTfm, (Join-Path $OutRoot 'csproj-backup'))
    }
}

$running = @(Get-Process -Name 'DaouCalendarOverlay' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Warning ('DaouCalendarOverlay 프로세스가 실행 중입니다(pid {0}). 빌드 산출물이 잠기거나 host 왕복에서 파이프 타임아웃이 빠질 수 있습니다.' -f (($running | ForEach-Object { $_.Id }) -join ', '))
}

New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null
$backupDir = Join-Path $OutRoot 'csproj-backup'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

# 원본 텍스트는 메모리에 담아 finally에서 되돌리고, 강제 종료 대비 사본은 $OutRoot 안에만 둔다.
$originals = @{}
$originalBytes = @{}
foreach ($p in $projectFiles) {
    $originalBytes[$p] = [System.IO.File]::ReadAllBytes($p)
    $originals[$p] = [System.IO.File]::ReadAllText($p)
    [System.IO.File]::WriteAllBytes((Join-Path $backupDir ([System.IO.Path]::GetFileName($p) + '.orig')), $originalBytes[$p])
}

# ---- 측정 ------------------------------------------------------------------------------------

$exitCode = 1
# dotnet CLI·MSBuild·vstest 출력을 영어로 고정한다. 한국어 UI 출력은 Windows PowerShell이 캡처할 때 인코딩이 어긋나
# 한글 줄이 깨지므로 테스트 요약과 실패 꼬리 줄을 읽을 수 없게 된다. 스크립트가 끝나면 원래 값으로 되돌린다.
$previousUiLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
Push-Location -LiteralPath $repo
try {
    $sdkRun = Invoke-Dotnet @('--version') -Quiet
    $sdkVersion = @($sdkRun.Lines | Where-Object { $_ -and $_.Trim() }) | Select-Object -First 1
    if (-not $sdkVersion) { $sdkVersion = "확인 실패(exit $($sdkRun.ExitCode))" }
    $startedAt = Get-Date

    Write-Host "저장소: $repo"
    Write-Host "시나리오: $BaselineTfm → $TrialTfm"
    Write-Host "출력: $OutRoot"
    Write-Host "dotnet SDK: $sdkVersion"
    Write-Host "csproj 원본 사본: $backupDir"

    $scenarios = New-Object System.Collections.Generic.List[object]
    $fatalError = $null
    $restoreFailures = New-Object System.Collections.Generic.List[string]

    try {
        foreach ($tfm in @($BaselineTfm, $TrialTfm)) {
            Write-Host ''
            Write-Host "=== 시나리오: $tfm ==="
            $scenario = [pscustomobject]@{ Tfm = $tfm; Build = $null; Publish = $null; Test = $null; Host = $null }
            $scenarios.Add($scenario)
            foreach ($p in $projectFiles) { Set-Tfm $p $tfm }

            Write-Host "[$tfm] 1/4 Release 빌드"
            $scenario.Build = Measure-Build
            Write-Host "[$tfm] 2/4 publish(win-x64, self-contained, single-file)"
            $scenario.Publish = Measure-Publish $tfm
            Write-Host "[$tfm] 3/4 dotnet test($testNote)"
            $scenario.Test = Measure-Test
            Write-Host "[$tfm] 4/4 host 모드 왕복"
            $scenario.Host = Measure-HostRoundTrip "$OutRoot\$tfm\DaouCalendarOverlay.exe"
        }
    }
    catch {
        $fatalError = $_.Exception.Message
    }
    finally {
        # 예외나 Ctrl+C로 빠져나와도 두 csproj를 반드시 원본 텍스트로 되돌린다.
        foreach ($p in $projectFiles) {
            try {
                $orig = $originals[$p]
                [System.IO.File]::WriteAllText($p, $orig, (New-Object System.Text.UTF8Encoding($false)))
                # 원본에 BOM이 있었다면 텍스트 복원만으로는 바이트가 달라지므로 원본 바이트를 그대로 쓴다.
                if ([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($p)) -ne [Convert]::ToBase64String($originalBytes[$p])) {
                    [System.IO.File]::WriteAllBytes($p, $originalBytes[$p])
                }
            }
            catch {
                $restoreFailures.Add(('{0}: {1}' -f $p, $_.Exception.Message))
            }
        }
    }

    if ($null -ne $fatalError) { Write-Warning "측정 중단: $fatalError" }

    foreach ($p in $projectFiles) {
        $currentBytes = [System.IO.File]::ReadAllBytes($p)
        if ([Convert]::ToBase64String($currentBytes) -ne [Convert]::ToBase64String($originalBytes[$p])) {
            $restoreFailures.Add(('{0}: 원본과 내용이 다릅니다. {1} 의 사본으로 되돌리세요.' -f $p, $backupDir))
        }
    }
    foreach ($failure in $restoreFailures) { Write-Warning "csproj 원복 실패: $failure" }

    # ---- 결과 표 -----------------------------------------------------------------------------

    $baseline = if ($scenarios.Count -ge 1) { $scenarios[0] } else { $null }
    $trial = if ($scenarios.Count -ge 2) { $scenarios[1] } else { $null }
    $finishedAt = Get-Date

    $report = New-Object System.Collections.Generic.List[string]
    $report.Add('# .NET 10 시험 측정 결과')
    $report.Add('')
    $report.Add("| 항목 | $BaselineTfm | $TrialTfm | 비고 |")
    $report.Add('|---|---|---|---|')
    $report.Add((New-Row 'Release 빌드 시간' (Format-TimeCell $baseline.Build) (Format-TimeCell $trial.Build) '벽시계, restore 포함. 이전 산출물(obj)이 있으면 증분 빌드'))
    $report.Add((New-Row '빌드 경고 수' (Format-WarningCell $baseline.Build) (Format-WarningCell $trial.Build) 'warning 포함 줄, 중복 제거'))
    $report.Add((New-Row 'publish 시간' (Format-TimeCell $baseline.Publish) (Format-TimeCell $trial.Publish) 'win-x64 self-contained single-file'))
    $report.Add((New-Row 'publish EXE 크기' (Format-ExeSizeCell $baseline.Publish) (Format-ExeSizeCell $trial.Publish) ''))
    $report.Add((New-Row 'dotnet test 결과' (Format-TestCell $baseline.Test) (Format-TestCell $trial.Test) $testNote))
    $report.Add((New-Row 'host 모드 왕복(중앙값)' (Format-HostCell $baseline.Host) (Format-HostCell $trial.Host) $hostNote))
    $report.Add('')
    $report.Add("- dotnet --version: $sdkVersion")
    $report.Add(('- 측정 일시(로컬): {0} ~ {1}' -f $startedAt.ToString('yyyy-MM-dd HH:mm:ss zzz'), $finishedAt.ToString('yyyy-MM-dd HH:mm:ss zzz')))
    $report.Add("- 머신 이름: $env:COMPUTERNAME")
    $report.Add(('- dotnet test 필터(두 시나리오 동일): `{0}` ({1})' -f $testFilter, $testNote))
    if ($SkipHostProbe) {
        $report.Add('- host 모드 왕복: -SkipHostProbe로 건너뜀')
    }
    else {
        $report.Add(('- host 모드 왕복: 게시 EXE를 `{0}` 인자로 {1}회 실행(stdin 프레임 1개 전송 → stdout 수신 → 종료). {2}' -f $hostOrigin, $HostRuns, $hostNote))
    }
    if ($restoreFailures.Count -eq 0) {
        $report.Add(('- csproj 원복: 확인됨(TargetFramework {0})' -f $BaselineTfm))
    }
    else {
        $report.Add(('- csproj 원복: 실패 {0}건. 사본: {1}' -f $restoreFailures.Count, $backupDir))
    }

    $report.Add('')
    $report.Add('## 실패 상세')
    $report.Add('')
    $failureCount = 0
    if ($null -ne $fatalError) {
        $failureCount++
        $report.Add("- 측정 중단: $fatalError")
        $report.Add('')
    }
    foreach ($failure in $restoreFailures) {
        $failureCount++
        $report.Add("- csproj 원복 실패: $failure")
        $report.Add('')
    }
    foreach ($s in $scenarios) {
        foreach ($step in @($s.Build, $s.Publish, $s.Test, $s.Host)) {
            if ($null -eq $step -or $step.Skipped -or $step.Success) { continue }
            $failureCount++
            $exitText = if ($null -ne $step.ExitCode) { "exit $($step.ExitCode)" } else { 'exit 없음' }
            $report.Add(('### {0} · {1} 실패 ({2})' -f $s.Tfm, $step.Name, $exitText))
            $report.Add('')
            $report.Add('```text')
            foreach ($line in @($step.Tail)) { $report.Add([string]$line) }
            $report.Add('```')
            $report.Add('')
        }
    }
    if ($failureCount -eq 0) { $report.Add('없음') }

    if (-not $SkipHostProbe) {
        $report.Add('')
        $report.Add('## host 응답(첫 응답 앞부분)')
        foreach ($s in $scenarios) {
            $response = if ($null -ne $s.Host -and $s.Host.Summary) { [string]$s.Host.Summary } else { '(응답 없음)' }
            $report.Add('')
            $report.Add("- $($s.Tfm)")
            $report.Add('')
            $report.Add('```text')
            $report.Add($response)
            $report.Add('```')
        }
    }

    $resultPath = Join-Path $OutRoot 'result.md'
    Write-Host ''
    foreach ($line in $report) { Write-Host $line }
    [System.IO.File]::WriteAllText($resultPath, (($report -join "`n") + "`n"), $utf8NoBom)
    Write-Host ''
    Write-Host "결과 파일: $resultPath"

    # ---- 원복 후 Release 빌드 --------------------------------------------------------------------

    Write-Host ''
    Write-Host "=== 원복 후 Release 빌드($BaselineTfm 산출물 복구) ==="
    $finalBuild = Invoke-Dotnet @('build', $solution, '-c', 'Release')
    $finalOk = ($finalBuild.ExitCode -eq 0)
    $finalSection = New-Object System.Collections.Generic.List[string]
    $finalSection.Add('')
    $finalSection.Add('## 원복 후 Release 빌드')
    $finalSection.Add('')
    if ($finalOk) {
        $finalSection.Add(('성공 (exit 0, {0})' -f (Format-Ms $finalBuild.ElapsedMs)))
    }
    else {
        $finalSection.Add(('실패 (exit {0}, {1})' -f $finalBuild.ExitCode, (Format-Ms $finalBuild.ElapsedMs)))
        $finalSection.Add('')
        $finalSection.Add('```text')
        foreach ($line in @(Get-Tail $finalBuild.Lines)) { $finalSection.Add([string]$line) }
        $finalSection.Add('```')
    }
    foreach ($line in $finalSection) { Write-Host $line }
    [System.IO.File]::AppendAllText($resultPath, (($finalSection -join "`n") + "`n"), $utf8NoBom)

    $anyFailure = ($null -ne $fatalError) -or ($restoreFailures.Count -gt 0) -or ($scenarios.Count -lt 2) -or (-not $finalOk)
    foreach ($s in $scenarios) {
        foreach ($step in @($s.Build, $s.Publish, $s.Test, $s.Host)) {
            if ($null -eq $step -or -not $step.Success) { $anyFailure = $true }
        }
    }
    $exitCode = if ($anyFailure) { 1 } else { 0 }
    Write-Host ''
    Write-Host ('종료 코드: {0}' -f $exitCode)
}
finally {
    Pop-Location
    $env:DOTNET_CLI_UI_LANGUAGE = $previousUiLanguage
}

exit $exitCode
