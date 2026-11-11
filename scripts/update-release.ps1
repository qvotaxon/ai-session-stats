<#
.SYNOPSIS
  Applies the latest stable GitHub release and restores the previous install if it fails.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ExpectedTag,
    [string] $InstallPath = (Join-Path $env:LOCALAPPDATA 'SessionInsights\app'),
    [int] $Port = 5180,
    [ValidatePattern('^SessionInsightsUpdate-[a-f0-9]{32}$')]
    [string] $UpdateTaskName
)

$ErrorActionPreference = 'Stop'
$repository = 'qvotaxon/ai-session-stats'
$releaseApi = "https://api.github.com/repos/$repository/releases/latest"
$dataRoot = Split-Path -Parent $InstallPath
$logPath = Join-Path $dataRoot 'update.log'
$packageRoot = Join-Path $dataRoot ('update-' + [guid]::NewGuid().ToString('N'))
$backupPath = Join-Path $dataRoot ('app-backup-' + (Get-Date -Format 'yyyyMMddHHmmss'))
$failedPath = Join-Path $dataRoot ('app-failed-' + [guid]::NewGuid().ToString('N'))
$oldTrayInstalled = $false
$oldTrayDefinition = $null
$trayStopped = $false
$serverStopped = $false
$appMoved = $false
$lockPath = Join-Path $dataRoot 'update.lock'
$lockStream = $null
$failureMessage = $null

function Write-UpdateLog([string] $message) {
    Add-Content -Path $logPath -Value "[$(Get-Date -Format o)] $message"
}

function Open-UpdateLock([string] $path) {
    try {
        return [System.IO.FileStream]::new($path, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::DeleteOnClose)
    } catch {
        $exception = $_.Exception.GetBaseException()
        $errorCode = $exception.HResult -band 0xffff
        if ($exception -is [System.IO.IOException] -and $errorCode -in @(32, 33)) {
            throw 'Another update is already in progress. Close any previous update error dialog and try again.'
        }
        throw "Could not acquire the update lock '${path}': $($exception.Message)"
    }
}

function Merge-JsonConfiguration([object] $defaults, [object] $overrides) {
    foreach ($property in $overrides.PSObject.Properties) {
        $defaultProperty = $defaults.PSObject.Properties[$property.Name]
        if ($defaultProperty -and
            $defaultProperty.Value -is [System.Management.Automation.PSCustomObject] -and
            $property.Value -is [System.Management.Automation.PSCustomObject]) {
            Merge-JsonConfiguration $defaultProperty.Value $property.Value | Out-Null
        } else {
            $defaults | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }
    return $defaults
}

function Merge-JsonConfigurationFile([string] $defaultPath, [string] $overridePath) {
    if (Test-Path -LiteralPath $defaultPath) {
        $defaults = Get-Content -LiteralPath $defaultPath -Raw | ConvertFrom-Json
        $overrides = Get-Content -LiteralPath $overridePath -Raw | ConvertFrom-Json
        $merged = Merge-JsonConfiguration $defaults $overrides
        $merged | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $defaultPath -Encoding UTF8
    } else {
        Copy-Item -LiteralPath $overridePath -Destination $defaultPath
    }
}

function Stop-ProcessesAndWait([System.Diagnostics.Process[]] $processes) {
    foreach ($process in $processes) {
        try {
            if (-not $process.HasExited) {
                try {
                    $process.Kill()
                } catch {
                    if (-not $process.HasExited) { throw }
                }
            }
            if (-not $process.WaitForExit(15000)) {
                throw "Process $($process.Id) did not exit within 15 seconds."
            }
        } finally {
            $process.Dispose()
        }
    }
}

function Move-InstallDirectory([string] $source, [string] $destination) {
    $deadline = (Get-Date).AddSeconds(60)
    while ($true) {
        try {
            [System.IO.Directory]::Move($source, $destination)
            return
        } catch {
            $exception = $_.Exception.GetBaseException()
            $errorCode = $exception.HResult -band 0xffff
            # 5 (access denied) is what a directory move reports while a process, or Defender scanning a new executable, still holds a file in it.
            $isLock = ($exception -is [System.IO.IOException] -and $errorCode -in @(32, 33)) -or
                $exception -is [System.UnauthorizedAccessException]
            if (-not $isLock -or (Get-Date) -ge $deadline) {
                throw
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Stop-TaskAndProcesses([string] $taskName, [string] $processName, [string] $path) {
    $processes = @(Get-Process -Name $processName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $path } |
        ForEach-Object { $null = $_.Handle; $_ })
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }

    Stop-ProcessesAndWait $processes
}

function Stop-Tray {
    $trayPath = Join-Path $InstallPath 'tray.ps1'
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" |
        Where-Object { $_.CommandLine -like "*$trayPath*" } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } |
        ForEach-Object { $null = $_.Handle; $_ })
    if (Get-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction Stop
        Unregister-ScheduledTask -TaskName 'SessionInsightsTray' -Confirm:$false
    }

    Stop-ProcessesAndWait $processes
}

function Write-ServerDiagnostics {
    try {
        $task = Get-ScheduledTask -TaskName 'SessionInsights' -ErrorAction Stop
        $info = Get-ScheduledTaskInfo -TaskName 'SessionInsights' -ErrorAction Stop
        $result = '0x{0:X8}' -f [uint32]$info.LastTaskResult
        Write-UpdateLog "Server task state: $($task.State); last result: $result; last run: $($info.LastRunTime.ToString('o'))."
        foreach ($action in $task.Actions) {
            Write-UpdateLog "Server task action: $($action.Execute) $($action.Arguments); working directory: $($action.WorkingDirectory)."
        }
    } catch {
        Write-UpdateLog "Could not read server task diagnostics: $($_.Exception.Message)"
    }

    try {
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop |
            Where-Object { $_.LocalPort -eq $Port })
        if (-not $listeners) {
            Write-UpdateLog "No TCP listener was found on port $Port."
        }
        foreach ($listener in $listeners) {
            $owner = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
            Write-UpdateLog "Port $Port listener: $($listener.LocalAddress); process $($owner.Id); executable $($owner.Path)."
        }
    } catch {
        Write-UpdateLog "Could not read port diagnostics: $($_.Exception.Message)"
    }
}

function Restore-PreviousInstall {
    Stop-TaskAndProcesses 'SessionInsights' 'SessionInsights.Web' (Join-Path $InstallPath 'SessionInsights.Web.exe')
    Stop-Tray
    if ($appMoved -and (Test-Path $InstallPath)) {
        Move-InstallDirectory $InstallPath $failedPath
        Write-UpdateLog "Failed application retained at $failedPath."
    }
    if (Test-Path $backupPath) {
        Move-InstallDirectory $backupPath $InstallPath
    }

    $installTask = Join-Path $packageRoot 'scripts\install-task.ps1'
    & $installTask -InstallPath $InstallPath -PublishedApp -SkipUpdaterCopy
    if ($oldTrayInstalled) {
        & (Join-Path $packageRoot 'scripts\install-tray.ps1') -InstallPath $InstallPath -Port $Port -SkipTrayCopy
    }
}

function Get-ReleaseAssets {
    $zipName = 'SessionInsights-win-x64.zip'
    $checksumName = "$zipName.sha256"
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $release = Invoke-RestMethod -Uri $releaseApi -Headers $headers -TimeoutSec 15
        if ($release.tag_name -ne $ExpectedTag -or $release.prerelease -or $release.draft) {
            throw 'The requested version is no longer the latest stable release.'
        }

        $zipAsset = $release.assets | Where-Object { $_.name -eq $zipName -and $_.state -eq 'uploaded' } | Select-Object -First 1
        $checksumAsset = $release.assets | Where-Object { $_.name -eq $checksumName -and $_.state -eq 'uploaded' } | Select-Object -First 1
        if ($zipAsset -and $checksumAsset) {
            return @($zipAsset, $checksumAsset)
        }
        if ($attempt -lt 5) {
            Write-UpdateLog "Release assets are not ready; retrying (attempt $attempt of 5)."
            Start-Sleep -Seconds 5
        }
    }
    throw 'The release is missing its application archive or SHA-256 checksum. Try again later.'
}

try {
    $oldTrayInstalled = [bool](Get-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction SilentlyContinue)
    if ($ExpectedTag -notmatch '^v\d+\.\d+\.\d+$') {
        throw 'The requested release tag is invalid.'
    }
    if (-not (Test-Path (Join-Path $InstallPath 'SessionInsights.Web.exe'))) {
        throw "The installed application was not found at $InstallPath."
    }
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    $lockStream = Open-UpdateLock $lockPath
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    Write-UpdateLog "Starting update to $ExpectedTag."

    if ($oldTrayInstalled) {
        $oldTrayDefinition = Export-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction Stop
    }
    $trayStopped = $true
    Stop-Tray

    $headers = @{ 'User-Agent' = 'SessionInsightsTray'; 'Cache-Control' = 'no-cache' }
    $zipName = 'SessionInsights-win-x64.zip'
    $checksumName = "$zipName.sha256"
    $zipAsset, $checksumAsset = Get-ReleaseAssets

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

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $packageRoot)
    $newAppPath = Join-Path $packageRoot 'app'
    if (-not (Test-Path (Join-Path $newAppPath 'SessionInsights.Web.exe')) -or
        -not (Test-Path (Join-Path $newAppPath 'wwwroot\index.html')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\install-task.ps1')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\install-tray.ps1')) -or
        -not (Test-Path (Join-Path $packageRoot 'scripts\tray.ps1')) -or
        -not (Test-Path (Join-Path $newAppPath 'update-release.ps1'))) {
        throw 'The verified release archive does not contain a complete application.'
    }

    Get-ChildItem -LiteralPath $InstallPath -Filter 'appsettings*.json' -File |
        ForEach-Object {
            Merge-JsonConfigurationFile -defaultPath (Join-Path $newAppPath $_.Name) -overridePath $_.FullName
        }

    $serverStopped = $true
    Write-UpdateLog 'Stopping the server before replacing the application.'
    Stop-TaskAndProcesses 'SessionInsights' 'SessionInsights.Web' (Join-Path $InstallPath 'SessionInsights.Web.exe')
    Write-UpdateLog "Moving installed application from $InstallPath to $backupPath."
    Move-InstallDirectory $InstallPath $backupPath
    $appMoved = $true
    Write-UpdateLog "Moving verified application from $newAppPath to $InstallPath."
    Move-InstallDirectory $newAppPath $InstallPath

    & (Join-Path $packageRoot 'scripts\install-task.ps1') -InstallPath $InstallPath -PublishedApp
    $healthUri = "http://127.0.0.1:$Port/health"
    # Defender's cloud check can hold a never-seen executable (every release has a new hash) for a minute or more on first run.
    $deadline = (Get-Date).AddSeconds(180)
    $healthy = $false
    $healthError = 'No health response received.'
    while ((Get-Date) -lt $deadline) {
        try {
            $healthy = (Invoke-WebRequest -Uri $healthUri -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200
        } catch {
            $healthError = $_.Exception.Message
            Start-Sleep -Seconds 1
        }
        if ($healthy) { break }
    }
    if (-not $healthy) {
        Write-UpdateLog "Last request to ${healthUri} failed: $healthError"
        Write-ServerDiagnostics
        throw 'The updated server did not become healthy within 180 seconds. See update.log for task and port diagnostics.'
    }

    if ($oldTrayInstalled) {
        & (Join-Path $packageRoot 'scripts\install-tray.ps1') -InstallPath $InstallPath -Port $Port
    }
    Write-UpdateLog "Updated successfully to $ExpectedTag. Previous app retained at $backupPath."
}
catch {
    $message = $_.Exception.Message
    Write-UpdateLog "Update failed: $message"
    Write-UpdateLog "$($_.InvocationInfo.PositionMessage)`n$($_.ScriptStackTrace)"
    if ($serverStopped) {
        try {
            Write-UpdateLog 'Restoring the previous application.'
            Restore-PreviousInstall
            Write-UpdateLog 'The previous application was restored.'
        } catch {
            $message = "$message`nRollback failed: $($_.Exception.Message)"
            Write-UpdateLog "Rollback failed: $($_.Exception.Message)"
        }
    } elseif ($oldTrayInstalled -and $lockStream) {
        try {
            if ($trayStopped -and $oldTrayDefinition) {
                Register-ScheduledTask -TaskName 'SessionInsightsTray' -Xml $oldTrayDefinition -Force -ErrorAction Stop | Out-Null
            } else {
                Stop-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction Stop
            }
            Start-ScheduledTask -TaskName 'SessionInsightsTray' -ErrorAction Stop
            Write-UpdateLog 'The previous tray task was restored.'
        } catch {
            $message = "$message`nCould not restore the tray: $($_.Exception.Message)"
            Write-UpdateLog "Could not restore the tray: $($_.Exception.Message)"
        }
    }
    $failureMessage = $message
}
finally {
    if (Test-Path $packageRoot) {
        Remove-Item $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($lockStream) {
        $lockStream.Dispose()
    }
    if ($UpdateTaskName) {
        try {
            Unregister-ScheduledTask -TaskName $UpdateTaskName -Confirm:$false -ErrorAction Stop
        } catch {
            Write-UpdateLog "Could not remove updater task ${UpdateTaskName}: $($_.Exception.Message)"
        }
    }
}

if ($failureMessage) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("Session Insights could not be updated.`n$failureMessage", 'Session Insights update failed', 'OK', 'Error') | Out-Null
}
