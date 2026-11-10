$ErrorActionPreference = 'Stop'
$updater = Join-Path (Split-Path -Parent $PSScriptRoot) 'update-release.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SessionInsights-lock-test-' + [guid]::NewGuid().ToString('N'))
$installPath = Join-Path $testRoot 'app'
$lockPath = Join-Path $testRoot 'update.lock'
$state = [pscustomobject]@{ TaskChanges = 0; DialogReached = $false; Contended = $false; Owner = $null }

function Assert([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}
function Get-ScheduledTask { param($TaskName, $ErrorAction) return [pscustomobject]@{ TaskName = $TaskName } }
function Get-CimInstance { param($ClassName, $Filter) return @() }
function Export-ScheduledTask { param($TaskName, $ErrorAction) return '<Task />' }
function Stop-ScheduledTask { param($TaskName, $ErrorAction) $state.TaskChanges++ }
function Unregister-ScheduledTask { param($TaskName, [switch]$Confirm, $ErrorAction) $state.TaskChanges++ }
function Register-ScheduledTask { param($TaskName, $Xml, [switch]$Force, $ErrorAction) $state.TaskChanges++ }
function Start-ScheduledTask { param($TaskName, $ErrorAction) $state.TaskChanges++ }
function Invoke-RestMethod { param($Uri, $Headers, $TimeoutSec) throw 'Simulated download failure.' }
function Add-Type {
    param($AssemblyName)
    if ($AssemblyName -ne 'System.Windows.Forms') { throw 'Unexpected assembly load.' }
    $state.DialogReached = $true
    if ($state.Contended) {
        Assert ($state.TaskChanges -eq 0) 'A competing updater changed the active updater tasks.'
    } else {
        $probe = [System.IO.FileStream]::new($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $probe.Dispose()
    }
    throw 'Error dialog intercepted.'
}
function Invoke-FailingUpdate {
    $state.DialogReached = $false
    try {
        & $updater -ExpectedTag 'v1.0.4' -InstallPath $installPath
    } catch {
        if ($_.Exception.Message -ne 'Error dialog intercepted.') { throw }
    }
    Assert $state.DialogReached 'The updater did not reach its error dialog.'
}

try {
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $installPath 'SessionInsights.Web.exe') | Out-Null
    Invoke-FailingUpdate
    Assert ($state.TaskChanges -eq 4) 'The failed updater did not stop and restore the tray.'
    Assert ((Get-Content (Join-Path $testRoot 'update.log') -Raw) -like '*Simulated download failure*') 'The original update failure was not logged.'

    $state.Contended = $true
    $state.TaskChanges = 0
    $state.Owner = [System.IO.FileStream]::new($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    Invoke-FailingUpdate
    Assert ($state.Owner.CanRead) 'The competing updater closed the owner lock.'
    if ($env:OS -eq 'Windows_NT') {
        Assert ((Get-Content (Join-Path $testRoot 'update.log') -Raw) -like '*Another update is already in progress*') 'A Windows sharing violation was not identified.'
    }
    $state.Owner.Dispose()
    $state.Owner = $null

    $state.Contended = $false
    $state.TaskChanges = 0
    Invoke-FailingUpdate
    Assert ($state.TaskChanges -eq 4) 'An unlocked leftover lock file blocked a retry.'

    $state.Contended = $true
    $state.TaskChanges = 0
    Remove-Item -LiteralPath $lockPath -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $lockPath | Out-Null
    Invoke-FailingUpdate
    $log = Get-Content (Join-Path $testRoot 'update.log') -Raw
    Assert ($log -like '*Could not acquire the update lock*') 'An access failure was misreported as a competing update.'
} finally {
    if ($state.Owner) { $state.Owner.Dispose() }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}

Write-Host 'Update lock tests passed.'
