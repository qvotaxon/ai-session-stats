#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Stops and removes the "SessionInsights" Windows Service. Keeps the install folder and %LOCALAPPDATA%\SessionInsights (token, reports) unless -RemoveFiles is given.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $InstallPath = (Join-Path $env:ProgramFiles 'SessionInsights'),
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'Stop'
$serviceName = 'SessionInsights'

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($serviceName, 'Stop and delete service')) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $serviceName | Out-Null
        Write-Host "Service '$serviceName' removed."
    }
} else {
    Write-Host "Service '$serviceName' is not installed."
}

if ($RemoveFiles -and (Test-Path $InstallPath) -and $PSCmdlet.ShouldProcess($InstallPath, 'Delete install folder')) {
    Remove-Item -Recurse -Force $InstallPath
    Write-Host "Removed $InstallPath"
}
