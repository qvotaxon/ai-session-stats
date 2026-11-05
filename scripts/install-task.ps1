<#
.SYNOPSIS
  Publishes Session Insights and starts it at every logon through a Scheduled Task (no admin, no password).
.DESCRIPTION
  The task runs as the current user, so it reads %USERPROFILE%\.copilot and .claude and launches the CLIs with your login.
#>
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app'),
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'
$taskName = 'SessionInsights'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'apps\backend\src\Web\SessionInsights.Web.csproj'
$dist = Join-Path $repo 'apps\frontend\dist'

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    throw "Task '$taskName' already exists. Run scripts\uninstall-task.ps1 first."
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
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

# conhost --headless keeps the console app from showing a window.
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" -Argument "--headless `"$exe`"" -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'Local dashboard for GitHub Copilot CLI and Claude Code sessions (127.0.0.1:5180).' | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host 'Installed and started. Open http://localhost:5180'
