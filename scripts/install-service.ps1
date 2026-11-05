#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Publishes Session Insights and installs it as the Windows Service "SessionInsights", running as a dedicated local account.
.DESCRIPTION
  Entra-joined (cloud) accounts cannot be used as a service logon account, so the service runs as a local account
  (default: SessionInsightsSvc) with a random password that only the service manager knows.
  The service reads YOUR session folders (.copilot, .claude) and runs the claude / copilot CLIs against your config
  folders through CLAUDE_CONFIG_DIR and COPILOT_HOME, so no second login is needed for Claude.
  The script grants that account access to those folders only. Run it from an elevated PowerShell.
.PARAMETER ProfilePath
  Profile of the user whose sessions are shown. Defaults to the current user's profile.
.PARAMETER CopilotToken
  Optional GitHub token for the Copilot CLI (it stores its login in the user's Credential Manager, which the service account cannot read).
  Stored in the service's registry key, readable by administrators only.
#>
[CmdletBinding()]
param(
    [string] $ServiceUser = 'SessionInsightsSvc',
    [string] $InstallPath = (Join-Path $env:ProgramFiles 'SessionInsights'),
    [string] $DataRoot = (Join-Path $env:ProgramData 'SessionInsights'),
    [string] $ProfilePath = $env:USERPROFILE,
    [System.Security.SecureString] $CopilotToken,
    [switch] $SkipFrontend
)

$ErrorActionPreference = 'Stop'
$serviceName = 'SessionInsights'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'apps\backend\src\Web\SessionInsights.Web.csproj'
$dist = Join-Path $repo 'apps\frontend\dist'
$account = "$env:COMPUTERNAME\$ServiceUser"

function Grant-Path([string] $Path, [string] $Rights, [switch] $Inherit) {
    # Recursive grants are slow on large trees; skip when a previous run already granted this account on the root.
    if ($Inherit -and ((Get-Acl $Path).Access | Where-Object { "$($_.IdentityReference)" -ieq $account })) { return }
    $spec = if ($Inherit) { "${account}:(OI)(CI)$Rights" } else { "${account}:$Rights" }
    icacls $Path /grant $spec | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Path" }
}

# Traverse (but not list) every folder between the profile and the target so the service can reach it.
function Grant-Traverse([string] $Path) {
    $dir = Split-Path -Parent $Path
    $stack = @()
    while ($dir -and $dir.Length -gt $ProfilePath.Length -and $dir.StartsWith($ProfilePath, [StringComparison]::OrdinalIgnoreCase)) {
        $stack += $dir
        $dir = Split-Path -Parent $dir
    }
    Grant-Path $ProfilePath '(X)'
    foreach ($d in $stack) { Grant-Path $d '(X)' }
}

function Add-LsaRight([string] $Sid, [string] $Right) {
    $cfg = Join-Path ([IO.Path]::GetTempPath()) "si-secpol-$([guid]::NewGuid()).inf"
    $db = "$cfg.sdb"
    try {
        secedit /export /cfg $cfg /areas USER_RIGHTS | Out-Null
        $text = Get-Content $cfg -Raw
        if ($text -match "(?m)^$Right\s*=\s*(.*)$") {
            if ($Matches[1] -notmatch [regex]::Escape($Sid)) {
                $text = $text -replace "(?m)^($Right\s*=\s*.*)$", "`$1,*$Sid"
            }
        } else {
            $text = $text -replace '(?m)^\[Privilege Rights\]\s*$', "[Privilege Rights]`r`n$Right = *$Sid"
        }
        Set-Content -Path $cfg -Value $text -Encoding Unicode
        secedit /configure /db $db /cfg $cfg /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit failed granting $Right" }
    } finally {
        Remove-Item $cfg, $db -ErrorAction SilentlyContinue
    }
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Service '$serviceName' already exists. Run scripts\uninstall-service.ps1 first."
}

foreach ($sub in '.copilot', '.claude') {
    if (-not (Test-Path (Join-Path $ProfilePath $sub))) { throw "Missing $(Join-Path $ProfilePath $sub). Check -ProfilePath." }
}

Write-Host "Publishing to $InstallPath ..."
dotnet publish $project --configuration Release --output $InstallPath --no-self-contained
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

if (-not $SkipFrontend) {
    if (-not (Test-Path (Join-Path $dist 'index.html'))) {
        throw "Frontend build not found at $dist. Run 'pnpm build' in apps\frontend, or pass -SkipFrontend."
    }
    $wwwroot = Join-Path $InstallPath 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $dist '*') -Destination $wwwroot -Recurse -Force
}

# 1. Local service account with a random password (never shown, never needed again except by the service manager).
$bytes = New-Object byte[] 24
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$plain = 'Si!' + [Convert]::ToBase64String($bytes)
$secure = ConvertTo-SecureString $plain -AsPlainText -Force
if (-not (Get-LocalUser -Name $ServiceUser -ErrorAction SilentlyContinue)) {
    New-LocalUser -Name $ServiceUser -Password $secure -PasswordNeverExpires -UserMayNotChangePassword `
        -Description 'Runs the Session Insights service' | Out-Null
} else {
    Set-LocalUser -Name $ServiceUser -Password $secure
}
$sid = (Get-LocalUser -Name $ServiceUser).SID.Value
Add-LsaRight $sid 'SeServiceLogonRight'
Add-LsaRight $sid 'SeDenyInteractiveLogonRight'
Add-LsaRight $sid 'SeDenyRemoteInteractiveLogonRight'

# 2. File access: data folder, the two session folders, and the CLI install folders.
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
Grant-Path $DataRoot 'M' -Inherit
Grant-Path $ProfilePath '(X)'
Grant-Path (Join-Path $ProfilePath '.copilot') 'M' -Inherit
Grant-Path (Join-Path $ProfilePath '.claude') 'M' -Inherit

$cliPaths = @{}
foreach ($cli in 'claude', 'copilot') {
    # A service cannot launch a .ps1 shim; prefer a real executable or .cmd shim.
    $cmd = Get-Command $cli -All -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -match '\.(exe|cmd)$' } | Select-Object -First 1
    if ($cmd) {
        $cliPaths[$cli] = $cmd.Source
        $cliDir = Split-Path -Parent $cmd.Source
        if ($cliDir.StartsWith($ProfilePath, [StringComparison]::OrdinalIgnoreCase)) {
            Grant-Traverse $cmd.Source
            if ($cmd.Source -match '\.cmd$') {
                # npm shim folder holds every global package: grant the shim files and the @github packages only.
                Grant-Path $cliDir '(RX)'
                Get-ChildItem $cliDir -File | ForEach-Object { icacls $_.FullName /grant "${account}:(RX)" | Out-Null }
                $scope = Join-Path $cliDir 'node_modules\@github'
                if (Test-Path $scope) {
                    Grant-Path (Join-Path $cliDir 'node_modules') '(X)'
                    icacls $scope /grant "${account}:(OI)(CI)RX" | Out-Null
                }
            } else {
                Grant-Path $cliDir 'RX' -Inherit
            }
        }
    } else {
        Write-Warning "Could not find '$cli' on PATH; set SessionInsights__$($cli)CliPath yourself."
    }
}

# 3. Register the service and its environment.
$exe = Join-Path $InstallPath 'SessionInsights.Web.exe'
$credential = New-Object System.Management.Automation.PSCredential($account, $secure)
New-Service -Name $serviceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName 'Session Insights' `
    -Description 'Local dashboard for GitHub Copilot CLI and Claude Code sessions (127.0.0.1:5180).' `
    -StartupType Automatic `
    -Credential $credential | Out-Null

$svcEnv = @(
    "SessionInsights__DataRoot=$DataRoot",
    "SessionInsights__CopilotRoot=$(Join-Path $ProfilePath '.copilot\session-state')",
    "SessionInsights__ClaudeRoot=$(Join-Path $ProfilePath '.claude\projects')",
    "SessionInsights__ClaudeConfigDir=$(Join-Path $ProfilePath '.claude')",
    "SessionInsights__CopilotHome=$(Join-Path $ProfilePath '.copilot')"
)
if ($cliPaths.ContainsKey('claude')) { $svcEnv += "SessionInsights__ClaudeCliPath=$($cliPaths['claude'])" }
if ($cliPaths.ContainsKey('copilot')) { $svcEnv += "SessionInsights__CopilotCliPath=$($cliPaths['copilot'])" }
if ($CopilotToken) {
    $svcEnv += "COPILOT_GITHUB_TOKEN=$([System.Net.NetworkCredential]::new('', $CopilotToken).Password)"
} else {
    Write-Warning 'No -CopilotToken given: Copilot report runs will fail to authenticate. Listing and Claude reports are unaffected.'
}
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name Environment -Value $svcEnv -Type MultiString

sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
Start-Service -Name $serviceName
Write-Host "Installed and started as $account. Open http://localhost:5180"
