<#
.SYNOPSIS
  Tray icon for Session Insights: click to open the dashboard, right-click for restart / quit.
#>
[CmdletBinding()]
param(
    [int] $Port = 5180,
    [string] $TaskName = 'SessionInsights'
)

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$url = "http://localhost:$Port"

# Draws a small bar-chart icon so the tray entry is recognisable without shipping an .ico file.
function New-TrayIcon([System.Drawing.Color] $accent) {
    $bmp = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 27))), 0, 0, 31, 31)
    $brush = New-Object System.Drawing.SolidBrush $accent
    $g.FillRectangle($brush, 7, 17, 4, 8)
    $g.FillRectangle($brush, 14, 10, 4, 15)
    $g.FillRectangle($brush, 21, 14, 4, 11)
    $g.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    return $icon
}

$upIcon = New-TrayIcon ([System.Drawing.Color]::FromArgb(255, 96, 165, 250))
$downIcon = New-TrayIcon ([System.Drawing.Color]::FromArgb(255, 239, 68, 68))

$notify = New-Object System.Windows.Forms.NotifyIcon
$notify.Icon = $downIcon
$notify.Text = 'Session Insights'
$notify.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$open = $menu.Items.Add('Open dashboard')
$open.Font = New-Object System.Drawing.Font($open.Font, [System.Drawing.FontStyle]::Bold)
$restart = $menu.Items.Add('Restart server')
$menu.Items.Add('-') | Out-Null
$quit = $menu.Items.Add('Quit tray icon')
$notify.ContextMenuStrip = $menu

$openAction = { Start-Process $url }
$open.add_Click($openAction)
$notify.add_Click({ if ($_.Button -eq 'Left') { Start-Process $url } })
$restart.add_Click({
    $notify.Icon = $downIcon
    $notify.Text = 'Session Insights: restarting'
    $script:graceUntil = (Get-Date).AddSeconds(4)
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    # Probe quickly until the server answers again; the tick handler restores the normal interval.
    $timer.Interval = 2000
    & $startProbe
})
$quit.add_Click({
    $notify.Visible = $false
    [System.Windows.Forms.Application]::Exit()
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 15000

# The probe runs in a background runspace so a stalled server never blocks the tray's UI thread.
$probeScript = {
    param($port)
    try {
        (Invoke-WebRequest "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200
    } catch { $false }
}
$script:probe = $null
$script:probeHandle = $null
$script:graceUntil = [DateTime]::MinValue

$startProbe = {
    if ($script:probe) { return }
    $script:probe = [powershell]::Create().AddScript($probeScript).AddArgument($Port)
    $script:probeHandle = $script:probe.BeginInvoke()
}

$pollTimer = New-Object System.Windows.Forms.Timer
$pollTimer.Interval = 250
$pollTimer.add_Tick({
    if (-not $script:probe -or -not $script:probeHandle.IsCompleted) { return }
    try { $up = [bool]($script:probe.EndInvoke($script:probeHandle) | Select-Object -First 1) } catch { $up = $false }
    $script:probe.Dispose()
    $script:probe = $null
    if ((Get-Date) -lt $script:graceUntil) { return }
    if ($up) { $timer.Interval = 15000 }
    $notify.Icon = if ($up) { $upIcon } else { $downIcon }
    $notify.Text = if ($up) { 'Session Insights: running' } else { 'Session Insights: not running' }
})

$timer.add_Tick($startProbe)
$timer.Start()
$pollTimer.Start()
& $startProbe

[System.Windows.Forms.Application]::Run()
$timer.Dispose()
$pollTimer.Dispose()
if ($script:probe) { $script:probe.Dispose() }
$notify.Dispose()
