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
$menu.ShowItemToolTips = $true
$open = $menu.Items.Add('Open dashboard')
$open.Font = New-Object System.Drawing.Font($open.Font, [System.Drawing.FontStyle]::Bold)
$restart = $menu.Items.Add('Restart server')
$versionItem = $menu.Items.Add('Installed version: checking...')
$versionItem.Enabled = $false
$checkForUpdates = $menu.Items.Add('Check for updates')
$updateAvailable = $menu.Items.Add('Update available')
$updateAvailable.Visible = $false
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
$script:latestTag = $null
$script:installedVersion = $null
$script:checkRequested = $false

$script:updateCheckScript = {
    param($port)
    $ErrorActionPreference = 'Stop'
    $current = Invoke-RestMethod -Uri "http://127.0.0.1:$port/version" -TimeoutSec 5
    if ($current.version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'The installed application returned an invalid version.'
    }

    $result = [pscustomobject]@{
        CurrentVersion = $current.version
        LatestVersion = $null
        LatestTag = $null
        Available = $false
        CheckError = $null
    }
    try {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/qvotaxon/ai-session-stats/releases/latest' `
            -Headers @{ 'User-Agent' = 'SessionInsightsTray'; 'Cache-Control' = 'no-cache' } -TimeoutSec 15
        $tag = [string]$release.tag_name
        if ($release.prerelease -or $release.draft -or $tag -notmatch '^v\d+\.\d+\.\d+$') {
            throw 'GitHub did not return a supported stable release.'
        }

        $currentVersion = [version]$current.version
        $latestVersion = [version]$tag.Substring(1)
        if ($latestVersion -gt $currentVersion) {
            $assetNames = @($release.assets | Where-Object { $_.state -eq 'uploaded' } | ForEach-Object { $_.name })
            if ('SessionInsights-win-x64.zip' -notin $assetNames -or
                'SessionInsights-win-x64.zip.sha256' -notin $assetNames) {
                throw 'The latest release is not ready for update yet. Try again later.'
            }
        }
        $result.LatestVersion = $latestVersion.ToString(3)
        $result.LatestTag = $tag
        $result.Available = ($latestVersion -gt $currentVersion)
    } catch {
        $result.CheckError = $_.Exception.GetBaseException().Message
        if ($_.ErrorDetails.Message -match 'API rate limit exceeded') {
            $result.CheckError = 'GitHub API rate limit reached. Try again after the rate limit resets.'
        }
    }
    return $result
}
$script:updateCheck = $null
$script:updateCheckHandle = $null
$script:startUpdateCheck = {
    if ($script:updateCheck) { return }
    $script:checkForUpdates.Enabled = $false
    $script:versionItem.Text = 'Installed version: checking...'
    $script:updateCheck = [powershell]::Create().AddScript($script:updateCheckScript).AddArgument($Port)
    $script:updateCheckHandle = $script:updateCheck.BeginInvoke()
}

$checkForUpdates.add_Click({
    $script:checkRequested = $true
    & $script:startUpdateCheck
})
$updateAvailable.add_Click({
    $tag = $script:latestTag
    if (-not $tag -or $tag -notmatch '^v\d+\.\d+\.\d+$') { return }
    $updater = Join-Path $PSScriptRoot 'update-release.ps1'
    if (-not (Test-Path $updater)) {
        $notify.ShowBalloonTip(5000, 'Session Insights', 'This installation cannot update itself. Install a release build first.', [System.Windows.Forms.ToolTipIcon]::Error)
        return
    }

    $updateTaskName = 'SessionInsightsUpdate-' + [guid]::NewGuid().ToString('N')
    try {
        $tempUpdater = Join-Path ([System.IO.Path]::GetTempPath()) ('SessionInsightsUpdate-' + [guid]::NewGuid().ToString('N') + '.ps1')
        Copy-Item $updater $tempUpdater -ErrorAction Stop
        $appPath = $PSScriptRoot
        $arguments = "-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$tempUpdater`" -ExpectedTag `"$tag`" -InstallPath `"$appPath`" -Port $Port -UpdateTaskName `"$updateTaskName`""
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\conhost.exe" -Argument "--headless powershell.exe $arguments" -WorkingDirectory ([System.IO.Path]::GetTempPath()) -ErrorAction Stop
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited -ErrorAction Stop
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -ErrorAction Stop
        Register-ScheduledTask -TaskName $updateTaskName -Action $action -Principal $principal -Settings $settings -ErrorAction Stop | Out-Null
        $notify.Visible = $false
        Start-ScheduledTask -TaskName $updateTaskName -ErrorAction Stop
    } catch {
        $notify.Visible = $true
        $notify.ShowBalloonTip(5000, 'Session Insights', "Could not start the update: $($_.Exception.Message)", [System.Windows.Forms.ToolTipIcon]::Error)
    }
})

$startProbe = {
    if ($script:probe) { return }
    $script:probe = [powershell]::Create().AddScript($probeScript).AddArgument($Port)
    $script:probeHandle = $script:probe.BeginInvoke()
}

$pollTimer = New-Object System.Windows.Forms.Timer
$pollTimer.Interval = 250
$pollTimer.add_Tick({
    if ($script:probe -and $script:probeHandle.IsCompleted) {
        try { $up = [bool]($script:probe.EndInvoke($script:probeHandle) | Select-Object -First 1) } catch { $up = $false }
        $script:probe.Dispose()
        $script:probe = $null
        if ((Get-Date) -ge $script:graceUntil) {
            if ($up) { $timer.Interval = 15000 }
            $notify.Icon = if ($up) { $upIcon } else { $downIcon }
            $notify.Text = if ($up) { 'Session Insights: running' } else { 'Session Insights: not running' }
        }
    }

    if ($script:updateCheck -and $script:updateCheckHandle.IsCompleted) {
        try {
            $result = $script:updateCheck.EndInvoke($script:updateCheckHandle) | Select-Object -First 1
            $script:installedVersion = $result.CurrentVersion
            $script:versionItem.Text = "Installed version: $($result.CurrentVersion)"
            $script:latestTag = $result.LatestTag
            $script:updateAvailable.Text = "Update to $($result.LatestTag)"
            $script:updateAvailable.Visible = [bool]$result.Available
            $script:checkForUpdates.ToolTipText = [string]$result.CheckError
            if ($result.CheckError) {
                if ($script:checkRequested) {
                    $notify.ShowBalloonTip(5000, 'Session Insights', "Could not check for updates: $($result.CheckError)", [System.Windows.Forms.ToolTipIcon]::Error)
                }
            } elseif ($result.Available -and $script:checkRequested) {
                $notify.ShowBalloonTip(5000, 'Session Insights', "Update available: $($result.LatestTag)", [System.Windows.Forms.ToolTipIcon]::Info)
            } elseif (-not $result.Available -and $script:checkRequested) {
                $notify.ShowBalloonTip(3000, 'Session Insights', 'You are up to date.', [System.Windows.Forms.ToolTipIcon]::Info)
            }
        } catch {
            $script:versionItem.Text = if ($script:installedVersion) {
                "Installed version: $($script:installedVersion) (server unavailable)"
            } else {
                'Installed version: unavailable'
            }
            $script:updateAvailable.Visible = $false
            if ($script:checkRequested) {
                $notify.ShowBalloonTip(5000, 'Session Insights', "Could not check for updates: $($_.Exception.GetBaseException().Message)", [System.Windows.Forms.ToolTipIcon]::Error)
            }
        } finally {
            $script:updateCheck.Dispose()
            $script:updateCheck = $null
            $script:checkForUpdates.Enabled = $true
            $script:checkRequested = $false
        }
    }
})

$timer.add_Tick($startProbe)
$updateCheckTimer = New-Object System.Windows.Forms.Timer
$updateCheckTimer.Interval = 14400000
$updateCheckTimer.add_Tick($script:startUpdateCheck)
$updateCheckTimer.Start()
$timer.Start()
$pollTimer.Start()
& $startProbe
& $script:startUpdateCheck

[System.Windows.Forms.Application]::Run()
$timer.Dispose()
$pollTimer.Dispose()
$updateCheckTimer.Dispose()
if ($script:probe) { $script:probe.Dispose() }
if ($script:updateCheck) { $script:updateCheck.Dispose() }
$notify.Dispose()
