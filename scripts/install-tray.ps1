<#
.SYNOPSIS
  Registers the Session Insights tray icon to start at every logon (no admin needed) and starts it now.
#>
[CmdletBinding()]
param(
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app'),
    [int] $Port = 5180
)

$ErrorActionPreference = 'Stop'
$taskName = 'SessionInsightsTray'
$source = Join-Path $PSScriptRoot 'tray.ps1'
$target = Join-Path $InstallPath 'tray.ps1'

if (-not (Test-Path $InstallPath)) { throw "Install folder $InstallPath not found. Run scripts\install-task.ps1 first." }
Copy-Item $source $target -Force

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

# Stopping the task does not always end the powershell child, which would leave a second tray icon behind.
Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
    Where-Object { $_.CommandLine -like '*SessionInsights*tray.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }

$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$psArgs = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$target`" -Port $Port"
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" -Argument "--headless powershell.exe $psArgs" -WorkingDirectory $InstallPath
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'Tray icon that opens the Session Insights dashboard.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host 'Tray icon installed. In Windows 11, drag it from the overflow (^) onto the taskbar to keep it visible.'
