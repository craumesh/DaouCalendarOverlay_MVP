$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "DaouCalendarOverlay\DaouCalendarOverlay.csproj"
$out = Join-Path $root "publish"

if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}

Write-Host "Publishing single-file EXE..."
dotnet restore $project

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out

$exePath = Join-Path $out "DaouCalendarOverlay.exe"
Write-Host ""
Write-Host "Publish completed: $exePath"
