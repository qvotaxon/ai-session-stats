<#
.SYNOPSIS
  Starts the Session Insights server and tray icon if they are not running, then opens the dashboard.
.DESCRIPTION
  Target of the Start Menu shortcut. Both parts normally start at logon; this brings them back after the tray icon was quit.
#>
[CmdletBinding()]
param(
    [int] $Port = 5180
)

foreach ($taskName in 'SessionInsights', 'SessionInsightsTray') {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($task -and $task.State -ne 'Running') {
        Start-ScheduledTask -TaskName $taskName
    }
}

Start-Process "http://localhost:$Port"
