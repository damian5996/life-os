param([string]$Serial)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$adb = Join-Path $repo '.tools/android-sdk/platform-tools/adb.exe'
$state = Invoke-RestMethod 'http://127.0.0.1:5081/api/environment' -TimeoutSec 5
if ($state.name -ne 'Dev') { throw 'Start the local Dev backend first.' }
if (!$Serial) {
    $devices = @(& $adb devices | Where-Object { $_ -match '\sdevice$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($devices.Count -ne 1) { throw 'Connect exactly one authorized phone, or supply -Serial.' }
    $Serial = $devices[0]
}
& $adb -s $Serial reverse tcp:5081 tcp:5081
if ($LASTEXITCODE -ne 0) { throw 'USB forwarding failed.' }
Write-Output 'LifeOS Dev connected through USB. Production connection unchanged.'
