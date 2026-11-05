<#
.SYNOPSIS
  Rebuilds the frontend, removes the Session Insights task and installs it again (publish + task registration).
.DESCRIPTION
  The tray icon is reinstalled too when it is already registered, or when -Tray is given.
  Data under %LOCALAPPDATA%\SessionInsights (token, reports) is kept.
#>
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app'),
    [switch] $Tray,
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Invoke-Step([string] $name, [scriptblock] $body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)." }
}

if (-not $SkipFrontend) {
    Push-Location (Join-Path $repo 'apps\frontend')
    try { Invoke-Step 'Build frontend' { pnpm build } } finally { Pop-Location }
}

$trayInstalled = [bool](Get-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue)

# Uninstall first: the running server locks the published DLLs.
Write-Host '==> Uninstall task' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'uninstall-task.ps1') -InstallPath $InstallPath

Write-Host '==> Install task' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'install-task.ps1') -InstallPath $InstallPath

if ($Tray -or $trayInstalled) {
    Write-Host '==> Install tray icon' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'install-tray.ps1') -InstallPath $InstallPath
}

Write-Host 'Done. Open http://localhost:5180' -ForegroundColor Green
