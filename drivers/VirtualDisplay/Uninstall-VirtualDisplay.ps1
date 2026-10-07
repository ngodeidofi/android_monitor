<#
.SYNOPSIS
    Uninstalls the IddCx Virtual Secondary Monitor for Windows.
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$devcon = Join-Path $scriptDir "DriverFiles\Control\Dependencies\devcon.exe"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "Administrator rights required to uninstall Windows drivers. Relaunching..."
    Start-Process powershell.exe -ArgumentList ("-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"") -Verb RunAs
    exit
}

Write-Host "Removing Virtual Display Device..." -ForegroundColor Yellow
if (Test-Path $devcon) {
    & $devcon remove "Root\MttVDD"
    Write-Host "Virtual display device removed." -ForegroundColor Green
} else {
    pnputil.exe /enum-devices /class Display
}

if (Test-Path "C:\IddSampleDriver") {
    Remove-Item -Path "C:\IddSampleDriver" -Recurse -Force -ErrorAction SilentlyContinue
}

# Restore to single display
& "$env:SystemRoot\System32\DisplaySwitch.exe" /internal
Write-Host "Windows display restored to PC screen only." -ForegroundColor Green
