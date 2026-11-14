param([string]$Serial)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'connect-dev-phone.ps1') -Serial $Serial
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
& $dotnet build (Join-Path $repo 'src/LifeOs.Mobile') -c Release -f net10.0-android -p:LifeOsEnvironment=Development "-p:AndroidSdkDirectory=$(Join-Path $repo '.tools/android-sdk')" "-p:JavaSdkDirectory=$(Join-Path $repo '.tools/jdk')" --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Dev APK build failed.' }
$adb = Join-Path $repo '.tools/android-sdk/platform-tools/adb.exe'
if (!$Serial) { $Serial = ((& $adb devices | Where-Object { $_ -match '\sdevice$' }) -split '\s+')[0] }
$services = & $adb -s $Serial shell dumpsys activity services pl.lifeos.mobile.dev
if ($services -match 'isForeground=true') { throw 'Stop recording in Dev before updating it.' }
$apk = Join-Path $repo 'src/LifeOs.Mobile/bin/Dev/Release/net10.0-android/pl.lifeos.mobile.dev-Signed.apk'
& $adb -s $Serial install -r $apk
if ($LASTEXITCODE -ne 0) { throw 'Dev installation failed.' }
Start-Sleep -Seconds 3
& $adb -s $Serial shell am force-stop pl.lifeos.mobile.dev
& $adb -s $Serial shell am start -n pl.lifeos.mobile.dev/pl.lifeos.mobile.MainActivity
