<#
.SYNOPSIS
  Applies the latest stable GitHub release and restores the previous install if it fails.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ExpectedTag,
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app'),
    [int] $Port = 5180
)

$ErrorActionPreference = 'Stop'
$repository = 'qvotaxon/ai-session-stats'
$releaseApi = "https://api.github.com/repos/$repository/releases/latest"
$dataRoot = Split-Path -Parent $InstallPath
$logPath = Join-Path $dataRoot 'update.log'
$packageRoot = Join-Path $dataRoot ('update-' + [guid]::NewGuid().ToString('N'))
$backupPath = Join-Path $dataRoot ('app-backup-' + (Get-Date -Format 'yyyyMMddHHmmss'))
$oldTrayInstalled = $false
$serverStopped = $false
$appMoved = $false

function Write-UpdateLog([string] $message) {
    Add-Content -Path $logPath -Value "[$(Get-Date -Format o)] $message"
}

function Stop-TaskAndProcesses([string] $taskName, [string] $processName, [string] $path) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }

    Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $path } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
}

function Stop-Tray {
    if (Get-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName 'SessionInsightsTray' -Confirm:$false
    }

    $trayPath = Join-Path $InstallPath 'tray.ps1'
    Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
        Where-Object { $_.CommandLine -like "*$trayPath*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

function Restore-PreviousInstall {
    Stop-TaskAndProcesses 'SessionInsights' 'SessionInsights.Web' (Join-Path $InstallPath 'SessionInsights.Web.exe')
    Stop-Tray
    if ($appMoved -and (Test-Path $InstallPath)) {
        Remove-Item $InstallPath -Recurse -Force
    }
    if (Test-Path $backupPath) {
        Move-Item $backupPath $InstallPath
    }

    $installTask = Join-Path $packageRoot 'scripts\install-task.ps1'
    & $installTask -InstallPath $InstallPath -PublishedApp
    if ($oldTrayInstalled) {
        & (Join-Path $packageRoot 'scripts\install-tray.ps1') -InstallPath $InstallPath
    }
}

try {
    if ($ExpectedTag -notmatch '^v\d+\.\d+\.\d+$') {
        throw 'The requested release tag is invalid.'
    }
    if (-not (Test-Path (Join-Path $InstallPath 'SessionInsights.Web.exe'))) {
        throw "The installed application was not found at $InstallPath."
    }
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    Write-UpdateLog "Starting update to $ExpectedTag."

    $headers = @{ 'User-Agent' = 'SessionInsightsTray' }
    $release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -TimeoutSec 15
    if ($release.tag_name -ne $ExpectedTag -or $release.prerelease -or $release.draft) {
        throw 'The requested version is no longer the latest stable release.'
    }

    $zipName = 'SessionInsights-win-x64.zip'
    $checksumName = "$zipName.sha256"
    $zipAsset = $release.assets | Where-Object { $_.name -eq $zipName } | Select-Object -First 1
    $checksumAsset = $release.assets | Where-Object { $_.name -eq $checksumName } | Select-Object -First 1
    if (-not $zipAsset -or -not $checksumAsset) {
        throw 'The release is missing its application archive or SHA-256 checksum.'
    }

    foreach ($asset in @($zipAsset, $checksumAsset)) {
        $uri = [Uri]$asset.browser_download_url
        $expectedPrefix = "/$repository/releases/download/$ExpectedTag/"
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or -not $uri.AbsolutePath.StartsWith($expectedPrefix, [StringComparison]::Ordinal)) {
            throw 'The release contains an unexpected download URL.'
        }
    }

    $archivePath = Join-Path $packageRoot $zipName
    $checksumPath = Join-Path $packageRoot $checksumName
    Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $archivePath -Headers $headers -TimeoutSec 120 -UseBasicParsing
    Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumPath -Headers $headers -TimeoutSec 30 -UseBasicParsing

    $checksumText = Get-Content -LiteralPath $checksumPath -Raw
    $checksumMatch = [regex]::Match($checksumText, '^\s*([a-fA-F0-9]{64})(?:\s+\*?SessionInsights-win-x64\.zip)?\s*$')
    if (-not $checksumMatch.Success) {
        throw 'The release checksum file is invalid.'
    }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $checksumMatch.Groups[1].Value, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The downloaded release failed its SHA-256 check.'
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $packageRoot -Force
    $newAppPath = Join-Path $packageRoot 'app'
    if (-not (Test-Path (Join-Path $newAppPath 'SessionInsights.Web.exe')) -or
        -not (Test-Path (Join-Path $newAppPath 'wwwroot\index.html')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\install-task.ps1')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\install-tray.ps1')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\tray.ps1')) -or
        -not (Test-Path (Join-Path $newAppPath 'update-release.ps1'))) {
        throw 'The verified release archive does not contain a complete application.'
    }

    $oldTrayInstalled = [bool](Get-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue)
    $serverStopped = $true
    Stop-Tray
    Stop-TaskAndProcesses 'SessionInsights' 'SessionInsights.Web' (Join-Path $InstallPath 'SessionInsights.Web.exe')
    Move-Item $InstallPath $backupPath
    $appMoved = $true
    Move-Item $newAppPath $InstallPath

    & (Join-Path $packageRoot 'scripts\install-task.ps1') -InstallPath $InstallPath -PublishedApp
    $healthUri = "http://127.0.0.1:$Port/health"
    $deadline = (Get-Date).AddSeconds(30)
    $healthy = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $healthy = (Invoke-WebRequest -Uri $healthUri -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200
        } catch {
            Start-Sleep -Seconds 1
        }
        if ($healthy) { break }
    }
    if (-not $healthy) {
        throw 'The updated server did not become healthy within 30 seconds.'
    }

    if ($oldTrayInstalled) {
        & (Join-Path $packageRoot 'scripts\install-tray.ps1') -InstallPath $InstallPath -Port $Port
    }
    Write-UpdateLog "Updated successfully to $ExpectedTag. Previous app retained at $backupPath."
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("Session Insights updated to $ExpectedTag.", 'Session Insights', 'OK', 'Information') | Out-Null
}
catch {
    $message = $_.Exception.Message
    Write-UpdateLog "Update failed: $message"
    if ($serverStopped) {
        try {
            Restore-PreviousInstall
            Write-UpdateLog 'The previous application was restored.'
        } catch {
            $message = "$message`nRollback failed: $($_.Exception.Message)"
            Write-UpdateLog "Rollback failed: $($_.Exception.Message)"
        }
    }
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("Session Insights could not be updated.`n$message", 'Session Insights update failed', 'OK', 'Error') | Out-Null
}
finally {
    if (Test-Path $packageRoot) {
        Remove-Item $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
