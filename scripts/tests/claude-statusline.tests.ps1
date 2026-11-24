$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$statusline = Join-Path $root 'apps/backend/src/Web/claude-statusline.ps1'
$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('SessionInsights-statusline-test-' + [guid]::NewGuid().ToString('N'))
$allowancePath = Join-Path $dataRoot 'claude-allowance.json'
$sample = '{"rate_limits":{"seven_day":{"used_percentage":10,"resets_at":1795000000}}}'

function Assert([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

function Invoke-Statusline {
    $output = $sample | & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $statusline 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
}

$previousDataRoot = $env:SessionInsights__DataRoot
try {
    $env:SessionInsights__DataRoot = $dataRoot

    $first = Invoke-Statusline
    Assert ($first.ExitCode -eq 0) "The first statusline run failed with exit code $($first.ExitCode): $($first.Output)"
    Assert (Test-Path -LiteralPath $allowancePath) 'The first statusline run did not write claude-allowance.json.'
    $firstSnapshot = Get-Content -LiteralPath $allowancePath -Raw | ConvertFrom-Json

    Start-Sleep -Milliseconds 50

    $second = Invoke-Statusline
    Assert ($second.ExitCode -eq 0) "The second statusline run failed with exit code $($second.ExitCode): $($second.Output)"
    $secondSnapshot = Get-Content -LiteralPath $allowancePath -Raw | ConvertFrom-Json
    Assert ($secondSnapshot.updatedAt -ne $firstSnapshot.updatedAt) 'The second statusline run did not rewrite claude-allowance.json.'
    Assert ($secondSnapshot.sevenDay.usedPercentage -eq 10) 'The second statusline run did not store the supplied allowance.'
    Assert (-not (Get-ChildItem -LiteralPath $dataRoot -Filter '*.tmp')) 'The statusline run left a temporary file behind.'
} finally {
    $env:SessionInsights__DataRoot = $previousDataRoot
    if (Test-Path -LiteralPath $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
}

Write-Host 'Claude statusline tests passed.'
