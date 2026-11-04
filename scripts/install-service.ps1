#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Publishes Session Insights and installs it as the Windows Service "SessionInsights".
.DESCRIPTION
  The service must run as YOUR account (not LocalSystem): it reads %USERPROFILE%\.copilot and .claude,
  stores its token under %LOCALAPPDATA%, and launches the claude / copilot CLIs with your login.
  Run from an elevated PowerShell. You will be asked for the account password.
#>
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:ProgramFiles 'SessionInsights'),
    [System.Management.Automation.PSCredential] $Credential = (Get-Credential -UserName "$env:USERDOMAIN\$env:USERNAME" -Message 'Account the service runs as (needs access to your Copilot/Claude folders)'),
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'
$serviceName = 'SessionInsights'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'apps\backend\src\Web\SessionInsights.Web.csproj'
$dist = Join-Path $repo 'apps\frontend\dist'

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Service '$serviceName' already exists. Run scripts\uninstall-service.ps1 first."
}

Write-Host "Publishing to $InstallPath ..."
dotnet publish $project --configuration Release --output $InstallPath --no-self-contained
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

if (-not $SkipFrontend) {
    if (-not (Test-Path (Join-Path $dist 'index.html'))) {
        throw "Frontend build not found at $dist. Run 'pnpm build' in apps\frontend, or pass -SkipFrontend."
    }
    $wwwroot = Join-Path $InstallPath 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $dist '*') -Destination $wwwroot -Recurse -Force
}

$exe = Join-Path $InstallPath 'SessionInsights.Web.exe'
New-Service -Name $serviceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName 'Session Insights' `
    -Description 'Local dashboard for GitHub Copilot CLI and Claude Code sessions (127.0.0.1:5180).' `
    -StartupType Automatic `
    -Credential $Credential | Out-Null

sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
Start-Service -Name $serviceName
Write-Host "Installed and started. Open http://localhost:5180"
