<#
.SYNOPSIS
  Removes the Session Insights and tray Scheduled Tasks and stops the running processes.
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

$trayTask = 'SessionInsightsTray'
if (Get-ScheduledTask -TaskName $trayTask -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($trayTask, 'Unregister scheduled task')) {
        Stop-ScheduledTask -TaskName $trayTask -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $trayTask -Confirm:$false
    }
}

# Stopping the task does not always end the powershell child that hosts the tray icon.
Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
    Where-Object { $_.CommandLine -like '*SessionInsights*tray.ps1*' } |
    ForEach-Object { if ($PSCmdlet.ShouldProcess("PID $($_.ProcessId)", 'Stop tray process')) { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } }

Write-Host "Removed. Data under $env:LOCALAPPDATA\SessionInsights (token, reports) is kept."
