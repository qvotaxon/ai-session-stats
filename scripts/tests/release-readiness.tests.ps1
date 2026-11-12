$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Get-ParsedScript([string] $path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    return $ast
}

$updaterAst = Get-ParsedScript (Join-Path $root 'scripts/update-release.ps1')
$functionAst = $updaterAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-ReleaseAssets'
}, $true)
if (-not $functionAst) { throw 'Get-ReleaseAssets not found.' }
Invoke-Expression $functionAst.Extent.Text

$trayAst = Get-ParsedScript (Join-Path $root 'scripts/tray.ps1')
$assignment = $trayAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left.Extent.Text -eq '$script:updateCheckScript'
}, $true)
if (-not $assignment) { throw 'Tray update check not found.' }
$trayCheck = Invoke-Expression $assignment.Right.Extent.Text

$script:releases = @()
$script:calls = 0
$script:sleeps = 0
$script:apiError = $null
$script:localError = $null
$script:localVersion = '1.0.3'
$releaseApi = 'https://api.github.com/repos/qvotaxon/ai-session-stats/releases/latest'
$headers = @{ 'Cache-Control' = 'no-cache' }
$ExpectedTag = 'v1.0.4'

function Invoke-RestMethod {
    param($Uri, $Headers, $TimeoutSec)
    if ($Uri -like 'http://127.0.0.1:*') {
        if ($script:localError) { Write-Error $script:localError; return }
        return [pscustomobject]@{ version = $script:localVersion }
    }
    if ($Headers['Cache-Control'] -ne 'no-cache') { throw 'Missing no-cache header.' }
    if ($script:apiError) {
        $record = [System.Management.Automation.ErrorRecord]::new(
            [System.Net.WebException]::new('The remote server returned an error: (403) Forbidden.'),
            'WebCmdletWebResponseException', [System.Management.Automation.ErrorCategory]::InvalidOperation, $Uri)
        $record.ErrorDetails = [System.Management.Automation.ErrorDetails]::new($script:apiError)
        Write-Error -ErrorRecord $record
        return
    }
    $index = [Math]::Min($script:calls, $script:releases.Count - 1)
    $script:calls++
    return $script:releases[$index]
}
function Start-Sleep { param($Seconds) $script:sleeps++ }
function Write-UpdateLog { param($message) }
function Assert([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}
function Assert-Throws([scriptblock] $action, [string] $expected) {
    try { & $action } catch {
        if ($_.Exception.Message -like "*$expected*") { return }
        throw
    }
    throw "Expected error containing '$expected'."
}
function New-Release([string] $tag, [object[]] $assets) {
    return [pscustomobject]@{ tag_name = $tag; prerelease = $false; draft = $false; assets = $assets }
}

$zip = [pscustomobject]@{ name = 'SessionInsights-win-x64.zip'; state = 'uploaded' }
$checksum = [pscustomobject]@{ name = 'SessionInsights-win-x64.zip.sha256'; state = 'uploaded' }
$incomplete = New-Release 'v1.0.4' @($zip)
$complete = New-Release 'v1.0.4' @($zip, $checksum)

$script:releases = @($incomplete, $complete)
$assets = @(Get-ReleaseAssets)
Assert ($assets.Count -eq 2 -and $assets[0] -eq $zip -and $assets[1] -eq $checksum) 'Updater did not return both assets.'
Assert ($script:calls -eq 2 -and $script:sleeps -eq 1) 'Updater did not retry once.'

$script:calls = 0
$script:sleeps = 0
$script:releases = @($incomplete)
Assert-Throws { Get-ReleaseAssets } 'missing its application archive or SHA-256 checksum'
Assert ($script:calls -eq 5 -and $script:sleeps -eq 4) 'Updater did not stop after the retry limit.'

$script:calls = 0
$script:releases = @($incomplete, (New-Release 'v1.0.5' @($zip, $checksum)))
Assert-Throws { Get-ReleaseAssets } 'no longer the latest stable release'
Assert ($script:calls -eq 2) 'Updater did not revalidate the tag on retry.'

$script:calls = 0
$script:releases = @($incomplete)
$result = & $trayCheck 5180
Assert ($result.CheckError -like '*not ready for update yet*' -and -not $result.Available) 'Tray offered an incomplete release.'
Assert ($result.CurrentVersion -eq '1.0.3') 'Tray lost the installed version on a GitHub error.'
$script:calls = 0
$script:releases = @($complete)
$result = & $trayCheck 5180
Assert ($result.Available -and $result.LatestTag -eq 'v1.0.4') 'Tray did not offer the complete release.'
$script:calls = 0
$script:releases = @((New-Release 'v1.0.3' @()))
$result = & $trayCheck 5180
Assert (-not $result.Available) 'Tray offered the installed release.'

foreach ($currentVersion in @('1.0.4-preview', '1.0.3-preview')) {
    $script:localVersion = $currentVersion
    $script:calls = 0
    $script:releases = @($complete)
    $result = & $trayCheck 5180
    Assert ($result.Available -and $result.LatestTag -eq 'v1.0.4') "Tray did not offer stable from $currentVersion."
    Assert ($result.CurrentVersion -eq $currentVersion) 'Tray hid the preview suffix.'
}
$script:localVersion = '1.0.5-preview'
$script:calls = 0
$result = & $trayCheck 5180
Assert (-not $result.Available) 'Tray offered a downgrade from a newer preview.'
$script:localVersion = '1.0.4-preview'
$script:releases = @($incomplete)
$script:calls = 0
$result = & $trayCheck 5180
Assert (-not $result.Available -and $result.CheckError -like '*not ready for update yet*') 'Tray offered an incomplete matching stable release to a preview.'
$script:localVersion = '1.0.3'

foreach ($kind in @('prerelease', 'draft', 'preview-tag')) {
    $unsupported = New-Release 'v1.0.4' @($zip, $checksum)
    if ($kind -eq 'preview-tag') {
        $unsupported.tag_name = 'v1.0.4-preview'
    } else {
        $unsupported.$kind = $true
    }
    $script:releases = @($unsupported)
    $script:calls = 0
    $result = & $trayCheck 5180
    Assert (-not $result.Available -and -not $result.LatestTag -and $result.CheckError) "Tray offered $kind to a stable install."
    $script:localVersion = '1.0.3-preview'
    $script:calls = 0
    $result = & $trayCheck 5180
    Assert (-not $result.Available -and $result.CheckError) "Tray offered $kind to a preview install."
    $script:localVersion = '1.0.3'
    $script:calls = 0
    Assert-Throws { Get-ReleaseAssets } 'no longer the latest stable release'
}

$script:localVersion = '1.0.3-invalid'
Assert-Throws { & $trayCheck 5180 } 'invalid version'
$script:localVersion = '1.0.3'

$zipPending = [pscustomobject]@{ name = $zip.name; state = 'new' }
$script:calls = 0
$script:releases = @((New-Release 'v1.0.4' @($zipPending, $checksum)))
$result = & $trayCheck 5180
Assert ($result.CheckError -like '*not ready for update yet*' -and -not $result.Available) 'Tray offered an asset still uploading.'
$script:calls = 0
Assert-Throws { Get-ReleaseAssets } 'missing its application archive or SHA-256 checksum'

$script:apiError = '{"message":"API rate limit exceeded for the network."}'
$result = & $trayCheck 5180
Assert ($result.CurrentVersion -eq '1.0.3') 'Rate limiting hid the installed version.'
Assert ($result.CheckError -like '*GitHub API rate limit reached*') 'Rate limiting was not reported accurately.'
Assert (-not $result.Available -and -not $result.LatestTag) 'A failed check left an update available.'
Assert-Throws { Get-ReleaseAssets } '403'

$runspace = [powershell]::Create()
try {
    $mock = 'function Invoke-RestMethod {' + (Get-Command Invoke-RestMethod).Definition + '}'
    $null = $runspace.AddScript($mock).AddScript('$script:localVersion = ''1.0.3''; $script:apiError = ''{"message":"API rate limit exceeded."}''; $ErrorActionPreference = ''Continue''')
    $null = $runspace.AddScript($trayCheck.ToString()).AddArgument(5180)
    $handle = $runspace.BeginInvoke()
    $results = @($runspace.EndInvoke($handle))
    Assert ($results.Count -eq 1 -and $results[0].CurrentVersion -eq '1.0.3') 'Background check did not preserve the installed version.'
    Assert ($results[0].CheckError -like '*GitHub API rate limit reached*') 'Background check masked a non-terminating HTTP error.'
} finally {
    $runspace.Dispose()
}

$script:apiError = $null
$script:localError = 'The local server is unavailable.'
Assert-Throws { & $trayCheck 5180 } 'local server is unavailable'
$script:localError = $null

$pollCall = $trayAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
        $node.Expression.Extent.Text -eq '$pollTimer' -and $node.Member.Value -eq 'add_Tick'
}, $true)
if (-not $pollCall) { throw 'Tray result handler not found.' }
$poll = $pollCall.Arguments[0].ScriptBlock.GetScriptBlock()
$script:probe = $null
$script:versionItem = [pscustomobject]@{ Text = 'Installed version: checking...' }
$script:updateAvailable = [pscustomobject]@{ Text = 'Update to v1.0.4'; Visible = $true }
$script:checkForUpdates = [pscustomobject]@{ Enabled = $false; ToolTipText = '' }
$script:checkRequested = $false
$script:updateCheckHandle = [pscustomobject]@{ IsCompleted = $true }
$script:updateCheck = [pscustomobject]@{}
$script:updateCheck | Add-Member ScriptMethod EndInvoke {
    param($handle)
    [pscustomobject]@{
        CurrentVersion = '1.0.3'
        LatestTag = $null
        Available = $false
        CheckError = 'GitHub API rate limit reached.'
    }
}
$script:updateCheck | Add-Member ScriptMethod Dispose {}
& $poll
Assert ($script:versionItem.Text -eq 'Installed version: 1.0.3') 'The menu hid the local version after a failed GitHub check.'
Assert (-not $script:updateAvailable.Visible -and -not $script:latestTag) 'The menu retained a stale update after a failed check.'
Assert ($script:checkForUpdates.ToolTipText -like '*rate limit reached*') 'The menu did not expose the background check error.'
Assert ($script:checkForUpdates.Enabled -and -not $script:updateCheck) 'The menu did not finish its update check.'

Write-Host 'Release readiness tests passed.'
