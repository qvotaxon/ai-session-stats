$ErrorActionPreference = "Stop"

function Get-RateLimit($limit) {
    if ($null -eq $limit -or $null -eq $limit.used_percentage) {
        return $null
    }

    $percentage = [double]$limit.used_percentage
    if ([double]::IsNaN($percentage) -or [double]::IsInfinity($percentage) -or $percentage -lt 0 -or $percentage -gt 100) {
        throw "Claude Code supplied an invalid rate-limit percentage."
    }

    $reset = $null
    if ($limit.resets_at) {
        $parsed = [DateTimeOffset]::MinValue
        if ([DateTimeOffset]::TryParse([string]$limit.resets_at, [ref]$parsed)) {
            $reset = $parsed.ToUniversalTime().ToString("O")
        }
    }

    return [ordered]@{
        usedPercentage = $percentage
        resetsAt = $reset
    }
}

$inputJson = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($inputJson)) {
    throw "Claude Code did not provide statusline input."
}

$inputData = ConvertFrom-Json -InputObject $inputJson
$rateLimits = $inputData.rate_limits
$snapshot = [ordered]@{
    updatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    fiveHour = Get-RateLimit $rateLimits.five_hour
    sevenDay = Get-RateLimit $rateLimits.seven_day
}

$dataRoot = $env:SessionInsights__DataRoot
if ([string]::IsNullOrWhiteSpace($dataRoot)) {
    $dataRoot = Join-Path $env:LOCALAPPDATA "SessionInsights"
}

[System.IO.Directory]::CreateDirectory($dataRoot) | Out-Null
$path = Join-Path $dataRoot "claude-allowance.json"
$temporaryPath = "$path.$PID.tmp"
$json = ConvertTo-Json -InputObject $snapshot -Depth 4 -Compress
[System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
if ([System.IO.File]::Exists($path)) {
    [System.IO.File]::Replace($temporaryPath, $path, $null)
} else {
    [System.IO.File]::Move($temporaryPath, $path)
}
