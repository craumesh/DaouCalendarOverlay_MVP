<#
.SYNOPSIS
    Measures the per-spawn cost of DaouCalendarOverlay.exe in native messaging host mode.

.DESCRIPTION
    Chrome's sendNativeMessage starts a new host process for every call. This script starts the
    EXE the same way Chrome does (fixed extension origin argument, redirected stdio), closes stdin
    immediately so NativeBridgeProtocol.ReadFrameAsync returns null and the host exits normally,
    and records the wall-clock time from process start to exit plus the process CPU time.

    The first iteration is a warm-up (single-file extraction / disk cache) and is discarded.
    Per-iteration values are written with Write-Verbose only; use -Verbose to see them.
    The script does not touch the registry or settings files. The host process may write to its
    own log directory (%LOCALAPPDATA%\DaouCalendarOverlay\logs).

.EXAMPLE
    .\tools\Measure-HostSpawn.ps1 -ExePath .\DaouCalendarOverlay\bin\Release\net8.0-windows\DaouCalendarOverlay.exe -Label dev
    Development build (dotnet build -c Release). Framework-dependent since T2.6, so the output no longer
    has a win-x64 subfolder and the .NET 8 desktop runtime must be installed.

.EXAMPLE
    .\tools\Measure-HostSpawn.ps1 -ExePath .\publish\DaouCalendarOverlay-7.1.0.exe -Label release
    Release build produced by publish.ps1 (self-contained single-file, versioned file name).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [ValidateRange(1, 100000)][int]$Iterations = 20,
    [string]$Label = "current"
)

# Same origin argument Chrome passes to a native messaging host (NativeBridgeProtocol.ExtensionOrigin).
$hostArguments = "chrome-extension://gkchgbpcbljkgabjcgjelacfkphcmhmi/ --parent-window=0"
# Safety net only: a host that ignores stdin EOF must not hang the measurement forever.
$exitTimeoutMs = 60000

if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    Write-Error "ExePath not found: $ExePath"
    exit 2
}

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).ProviderPath

function Get-Median([double[]]$Values) {
    $sorted = $Values | Sort-Object
    $count = $sorted.Count
    if ($count -eq 0) { return [double]::NaN }
    $mid = [int][Math]::Floor($count / 2)
    if ($count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

$elapsedSamples = New-Object System.Collections.Generic.List[double]
$cpuSamples = New-Object System.Collections.Generic.List[double]
$nonZeroExitCount = 0

for ($i = 0; $i -le $Iterations; $i++) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $resolvedExe
    $psi.Arguments = $hostArguments
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        # stdin EOF -> ReadFrameAsync returns null -> host returns without touching the pipe.
        $p.StandardInput.Close()
        if (-not $p.WaitForExit($exitTimeoutMs)) {
            try { $p.Kill() } catch { }
            throw "Host process did not exit within $exitTimeoutMs ms (iteration $i)."
        }
        $sw.Stop()

        $elapsedMs = $sw.Elapsed.TotalMilliseconds
        # Read CPU time before the process handle is disposed.
        $cpuMs = $p.TotalProcessorTime.TotalMilliseconds
        $exitCode = $p.ExitCode
    }
    finally {
        $p.Dispose()
    }

    if ($i -eq 0) {
        Write-Verbose ("[{0}] warm-up (discarded): elapsed={1:N1} ms cpu={2:N1} ms exit={3}" -f $Label, $elapsedMs, $cpuMs, $exitCode)
        continue
    }

    if ($exitCode -ne 0) { $nonZeroExitCount++ }
    $elapsedSamples.Add($elapsedMs)
    $cpuSamples.Add($cpuMs)
    Write-Verbose ("[{0}] #{1}: elapsed={2:N1} ms cpu={3:N1} ms exit={4}" -f $Label, $i, $elapsedMs, $cpuMs, $exitCode)
}

if ($nonZeroExitCount -gt 0) {
    Write-Warning "$nonZeroExitCount of $($elapsedSamples.Count) samples exited with a non-zero code. Check host-yyyyMMdd.log."
}

$elapsedArray = $elapsedSamples.ToArray()
$cpuArray = $cpuSamples.ToArray()
$elapsedStats = $elapsedArray | Measure-Object -Minimum -Maximum -Average
$cpuStats = $cpuArray | Measure-Object -Average

$result = [pscustomobject]@{
    Label           = $Label
    Samples         = $elapsedArray.Count
    ElapsedMinMs    = [Math]::Round($elapsedStats.Minimum, 1)
    ElapsedMedianMs = [Math]::Round((Get-Median $elapsedArray), 1)
    ElapsedAvgMs    = [Math]::Round($elapsedStats.Average, 1)
    ElapsedMaxMs    = [Math]::Round($elapsedStats.Maximum, 1)
    CpuMedianMs     = [Math]::Round((Get-Median $cpuArray), 1)
    CpuAvgMs        = [Math]::Round($cpuStats.Average, 1)
    ExePath         = $resolvedExe
}

Write-Host ("{0}: samples={1} elapsed ms min/median/avg/max={2}/{3}/{4}/{5} cpu ms median/avg={6}/{7}" -f `
    $result.Label, $result.Samples, $result.ElapsedMinMs, $result.ElapsedMedianMs, $result.ElapsedAvgMs, `
    $result.ElapsedMaxMs, $result.CpuMedianMs, $result.CpuAvgMs)
$result
exit 0
