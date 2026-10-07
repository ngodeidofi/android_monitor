<#
.SYNOPSIS
    Builds the official Windows Installer (Setup.exe) for PhoneMonitor.
#>
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   Building PhoneMonitor Windows Installer v$Version" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Publish .NET Server
Write-Host "`n[1/4] Publishing PhoneMonitor Server..." -ForegroundColor Yellow
$payloadDir = Join-Path $scriptDir "payload"
if (Test-Path $payloadDir) { Remove-Item -Path $payloadDir -Recurse -Force }
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

$serverCsproj = Join-Path $projectRoot "windows\PhoneMonitor.Server\PhoneMonitor.Server.csproj"
& dotnet publish $serverCsproj -c Release -o $payloadDir

# 2. Gather Bundled Tools (ADB, Driver, APK)
Write-Host "`n[2/4] Gathering Bundled Driver & Platform Tools..." -ForegroundColor Yellow
$driverDest = Join-Path $payloadDir "Driver"
$adbDest = Join-Path $payloadDir "platform-tools"
$apkDest = Join-Path $payloadDir "apk"

New-Item -ItemType Directory -Path $driverDest, $adbDest, $apkDest -Force | Out-Null

# Copy Driver files
Copy-Item (Join-Path $projectRoot "drivers\VirtualDisplay\DriverFiles\VirtualDisplayDriver\*") -Destination $driverDest -Force
Copy-Item (Join-Path $projectRoot "drivers\VirtualDisplay\DriverFiles\Control\Dependencies\devcon.exe") -Destination $driverDest -Force
Copy-Item (Join-Path $projectRoot "drivers\VirtualDisplay\vdd_settings.xml") -Destination $driverDest -Force

# Copy ADB
$adbCandidates = @(
    "C:\Users\AHNi-HI\AppData\Local\Android\Sdk\platform-tools\adb.exe",
    "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
)
$foundAdb = $adbCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($foundAdb) {
    $adbDir = Split-Path -Parent $foundAdb
    Copy-Item (Join-Path $adbDir "adb.exe") -Destination $adbDest -Force
    Copy-Item (Join-Path $adbDir "AdbWinApi.dll") -Destination $adbDest -Force
    Copy-Item (Join-Path $adbDir "AdbWinUsbApi.dll") -Destination $adbDest -Force
}

# Copy APK if exists
$apkPath = Join-Path $projectRoot "android\app\build\outputs\apk\debug\app-debug.apk"
if (Test-Path $apkPath) {
    Copy-Item $apkPath -Destination (Join-Path $apkDest "PhoneMonitor.apk") -Force
}

# 3. Locate Inno Setup Compiler
Write-Host "`n[3/4] Locating Inno Setup Compiler..." -ForegroundColor Yellow
$isccCandidates = @(
    "C:\Users\AHNi-HI\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Run: winget install JRSoftware.InnoSetup"
}

# 4. Compile Installer
Write-Host "`n[4/4] Compiling Windows Installer executable..." -ForegroundColor Yellow
$issPath = Join-Path $scriptDir "PhoneMonitorSetup.iss"
& $iscc $issPath

$outputExe = Join-Path $scriptDir "output\PhoneMonitor-Setup-$Version.exe"
if (Test-Path $outputExe) {
    $item = Get-Item $outputExe
    $sizeMb = [math]::Round($item.Length / 1MB, 2)
    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " SUCCESS! Installer generated at:" -ForegroundColor Green
    Write-Host " $outputExe ($sizeMb MB)" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Green
}
