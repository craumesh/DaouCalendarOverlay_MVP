[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$OutputDir = "",
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

# Release publish script.
# - Self-contained / single-file settings live in Properties/PublishProfiles/win-x64.pubxml.
# - Every native command (dotnet, signtool) goes through Invoke-Checked so a non-zero exit code stops the script.
# - -Version is a guard, not an override: it must equal <Version> in DaouCalendarOverlay.csproj.

$root = Split-Path -Parent $PSCommandPath
$project = Join-Path $root "DaouCalendarOverlay\DaouCalendarOverlay.csproj"
$out = if ($OutputDir) { $OutputDir } else { Join-Path $root "publish" }
$symbols = Join-Path $out "symbols"

function Get-ProjectVersion([string]$ProjectPath) {
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Project file not found: $ProjectPath"
    }

    [xml]$document = Get-Content -LiteralPath $ProjectPath -Raw -Encoding UTF8

    foreach ($name in @("Version", "FileVersion")) {
        $nodes = $document.SelectNodes("//*[local-name()='PropertyGroup']/*[local-name()='$name']")
        foreach ($node in $nodes) {
            $value = $node.InnerText.Trim()
            if ($value) {
                return $value
            }
        }
    }

    return "7.1.0"
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Path
    }

    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not $programFilesX86) {
        return $null
    }

    $kitsBin = Join-Path $programFilesX86 "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsBin -PathType Container)) {
        return $null
    }

    $versionDirs = Get-ChildItem -LiteralPath $kitsBin -Directory |
        Where-Object { $_.Name -match '^\d+(\.\d+){1,3}$' } |
        Sort-Object -Property @{ Expression = { [version]$_.Name } } -Descending

    foreach ($dir in $versionDirs) {
        $candidate = Join-Path $dir.FullName "x64\signtool.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return $null
}

$projectVersion = Get-ProjectVersion $project
$resolvedVersion = if ($Version) { $Version } else { $projectVersion }

if ($resolvedVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "Invalid version: $resolvedVersion"
}

if ($Version -and ($Version -ne $projectVersion)) {
    throw "Version mismatch: -Version $Version does not match project version $projectVersion. Update <Version>/<AssemblyVersion>/<FileVersion> in DaouCalendarOverlay.csproj and assemblyIdentity version in app.manifest first."
}

$artifact = "DaouCalendarOverlay-$resolvedVersion.exe"

if ($DryRun) {
    $sign = if ($CertificateThumbprint) { $CertificateThumbprint } else { "none" }
    Write-Output "VERSION=$resolvedVersion"
    Write-Output "PROJECT=$project"
    Write-Output "PUBLISH_DIR=$out"
    Write-Output "SYMBOLS_DIR=$symbols"
    Write-Output "ARTIFACT=$artifact"
    Write-Output "PROFILE=win-x64"
    Write-Output "SIGN=$sign"
    Write-Output "DRYRUN=1"
    exit 0
}

if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}

Write-Host "Publishing DaouCalendarOverlay $resolvedVersion (profile win-x64)..."
Invoke-Checked "dotnet" @("restore", $project, "-r", "win-x64")
Invoke-Checked "dotnet" @("publish", $project, "-c", "Release", "-p:PublishProfile=win-x64", "-p:Version=$resolvedVersion", "-o", $out)

$publishedExe = Join-Path $out "DaouCalendarOverlay.exe"
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published executable not found: $publishedExe"
}

$finalExe = Join-Path $out $artifact
Move-Item -LiteralPath $publishedExe -Destination $finalExe -Force

New-Item -ItemType Directory -Path $symbols -Force | Out-Null
$pdbFiles = @(Get-ChildItem -LiteralPath $out -Filter *.pdb -File)
foreach ($pdb in $pdbFiles) {
    Move-Item -LiteralPath $pdb.FullName -Destination $symbols -Force
}

if ($CertificateThumbprint) {
    $signtool = Find-SignTool
    if (-not $signtool) {
        throw "signtool.exe not found. Install the Windows SDK or add signtool.exe to PATH."
    }

    Invoke-Checked $signtool @("sign", "/sha1", $CertificateThumbprint, "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256", $finalExe)
}
else {
    Write-Host "Code signing skipped (no -CertificateThumbprint)."
}

Write-Host ""
Write-Host "Publish completed: $finalExe"
Write-Host "Symbols: $symbols"
