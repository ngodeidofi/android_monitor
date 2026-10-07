<#
.SYNOPSIS
    Installs and enables the IddCx Virtual Secondary Monitor for Windows.
.DESCRIPTION
    Installs the signed Virtual Display Driver (IddCx) and configures Windows
    to extend the desktop (Win+P -> Extend).
#>

param(
    [switch]$Uninstall,
    [switch]$NoExtend
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$controlDir = Join-Path $scriptDir "DriverFiles\Control"
$devcon = Join-Path $controlDir "Dependencies\devcon.exe"
$vddDir = Join-Path $controlDir "SignedDrivers\x86\VDD"
$customXml = Join-Path $scriptDir "vdd_settings.xml"

# Check Administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Elevating with Administrator privileges..." -ForegroundColor Yellow
    Start-Process powershell.exe -ArgumentList ("-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"") -Verb RunAs
    exit
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   PhoneMonitor - Virtual Secondary Monitor Setup (IddCx)   " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if ($Uninstall) {
    Write-Host "Uninstalling Virtual Display Driver..." -ForegroundColor Yellow
    if (Test-Path $devcon) {
        & $devcon remove "Root\MttVDD"
    }
    Write-Host "Driver removed. Display configuration restored." -ForegroundColor Green
    exit
}

# 1. Configure vdd_settings.xml
$destDir = "C:\IddSampleDriver"
if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}
if (Test-Path $customXml) {
    Copy-Item -Path $customXml -Destination "$destDir\vdd_settings.xml" -Force
    Write-Host "[1/3] Configured monitor resolutions at C:\IddSampleDriver\vdd_settings.xml" -ForegroundColor Green
}

# 2. Install device via devcon.exe
$infFile = Join-Path $vddDir "MttVDD.inf"
if (-not (Test-Path $infFile)) {
    # Fallback to driver folder
    $infFile = Join-Path $scriptDir "DriverFiles\VirtualDisplayDriver\MttVDD.inf"
}

if ((Test-Path $infFile) -and (Test-Path $devcon)) {
    Write-Host "[2/3] Installing and activating Virtual Secondary Display..." -ForegroundColor Yellow
    
    # Remove existing instance if any
    & $devcon remove "Root\MttVDD" | Out-Null
    
    # Install new device
    $installOutput = & $devcon install $infFile "Root\MttVDD"
    $installOutput | Out-String | Write-Host

    Write-Host "`nVirtual Display Device installed successfully!" -ForegroundColor Green
} else {
    Write-Error "Driver files or devcon.exe missing in $controlDir"
    exit
}

# 3. Switch Windows to Extended Desktop (Win + P -> Extend)
if (-not $NoExtend) {
    Write-Host "[3/3] Switching Windows Display Configuration to EXTEND (Win + P)..." -ForegroundColor Cyan
    Start-Sleep -Seconds 1
    & "$env:SystemRoot\System32\DisplaySwitch.exe" /extend
    Write-Host "Desktop switched to Extended Display mode." -ForegroundColor Green
}

Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host " SUCCESS! You now have a genuine secondary monitor in Windows." -ForegroundColor Green
Write-Host " You can press [Win + P] anytime to toggle Duplicate or Extend." -ForegroundColor Cyan
Write-Host " Open Windows Settings > System > Display to arrange monitors." -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan
