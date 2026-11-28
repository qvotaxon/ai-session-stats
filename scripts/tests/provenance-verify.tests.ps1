$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Get-ParsedScript([string] $path) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
    return $ast
}

# Extract Test-ReleaseProvenance from the updater so the real policy is exercised without network.
$updaterAst = Get-ParsedScript (Join-Path $root 'scripts/update-release.ps1')
$functionAst = $updaterAst.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-ReleaseProvenance'
}, $true)
if (-not $functionAst) { throw 'Test-ReleaseProvenance not found.' }
Invoke-Expression $functionAst.Extent.Text

$repository = 'qvotaxon/ai-session-stats'
$script:logged = @()
function Write-UpdateLog { param($message) $script:logged += $message }
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

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SessionInsights-provenance-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$archivePath = Join-Path $testRoot 'SessionInsights-win-x64.zip'
$bundlePath = Join-Path $testRoot 'SessionInsights-win-x64.zip.sigstore.json'
Set-Content -LiteralPath $archivePath -Value 'archive' -NoNewline
Set-Content -LiteralPath $bundlePath -Value '{}' -NoNewline

# A stand-in for the gh executable, run as a child process so its exit code does not stop the test.
# It prints $env:GH_MOCK_OUTPUT and exits with $env:GH_MOCK_EXIT.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $ghMock = Join-Path $testRoot 'gh-mock.cmd'
    Set-Content -LiteralPath $ghMock -Value @'
@echo off
if defined GH_MOCK_OUTPUT echo %GH_MOCK_OUTPUT%
exit /b %GH_MOCK_EXIT%
'@
} else {
    $ghMock = Join-Path $testRoot 'gh-mock.sh'
    Set-Content -LiteralPath $ghMock -Value @'
#!/bin/sh
[ -n "$GH_MOCK_OUTPUT" ] && echo "$GH_MOCK_OUTPUT"
exit "$GH_MOCK_EXIT"
'@
    & chmod +x $ghMock
}
$script:ghAvailable = $true
function Get-Command {
    param($Name, $CommandType, $ErrorAction)
    if ($Name -eq 'gh') {
        if (-not $script:ghAvailable) { return $null }
        return [pscustomobject]@{ Source = $ghMock }
    }
    throw "Unexpected Get-Command for $Name."
}

try {
    # 1. Success: gh verifies with the offline bundle; no warning, no throw.
    $env:GH_MOCK_EXIT = '0'
    $env:GH_MOCK_OUTPUT = 'Verification succeeded!'
    $script:ghAvailable = $true
    $script:logged = @()
    $warning = Test-ReleaseProvenance $archivePath $bundlePath
    Assert ($null -eq $warning) 'A successful verification should not return a warning.'
    Assert (($script:logged -join "`n") -like '*Build provenance verified*') 'A successful verification was not logged.'

    # 2. Failure: gh returns non-zero and no auth hint; must throw (fatal), aborting the update.
    $env:GH_MOCK_EXIT = '1'
    $env:GH_MOCK_OUTPUT = 'verification failed: no matching attestation found'
    Assert-Throws { Test-ReleaseProvenance $archivePath $bundlePath } 'failed build provenance verification'

    # 3a. gh missing: fall back with a warning, never throw.
    $script:ghAvailable = $false
    $warning = Test-ReleaseProvenance $archivePath $bundlePath
    Assert ($warning -and $warning -like '*not installed*') 'A missing gh should fall back with a visible warning.'
    $script:ghAvailable = $true

    # 3b. gh present but unauthenticated, and no offline bundle: fall back with a warning.
    $env:GH_MOCK_EXIT = '1'
    $env:GH_MOCK_OUTPUT = 'error: gh auth login required; HTTP 401 Unauthorized'
    $warning = Test-ReleaseProvenance $archivePath $null
    Assert ($warning -and $warning -like '*not authenticated*') 'An unauthenticated gh without a bundle should fall back with a warning.'

    # 3c. An auth-looking failure is still fatal when an offline bundle was provided (no excuse to skip).
    $env:GH_MOCK_EXIT = '1'
    $env:GH_MOCK_OUTPUT = 'error: 401 Unauthorized'
    Assert-Throws { Test-ReleaseProvenance $archivePath $bundlePath } 'failed build provenance verification'
} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\GH_MOCK_EXIT -ErrorAction SilentlyContinue
    Remove-Item Env:\GH_MOCK_OUTPUT -ErrorAction SilentlyContinue
}

Write-Host 'Provenance verification tests passed.'
