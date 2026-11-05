<#
.SYNOPSIS
  Removes the Session Insights Scheduled Task and stops the running process.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app')
)

$ErrorActionPreference = 'Stop'
$taskName = 'SessionInsights'

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($taskName, 'Unregister scheduled task')) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}

$exe = Join-Path $InstallPath 'SessionInsights.Web.exe'
Get-Process -Name 'SessionInsights.Web' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe } |
    ForEach-Object { if ($PSCmdlet.ShouldProcess("PID $($_.Id)", 'Stop process')) { Stop-Process -Id $_.Id } }

Write-Host "Removed. Data under $env:LOCALAPPDATA\SessionInsights (token, reports) is kept."
