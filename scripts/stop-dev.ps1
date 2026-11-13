$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$statePath = Join-Path $repo '.tools/dev/server.json'
if (Test-Path $statePath) {
    $state = Get-Content $statePath -Raw | ConvertFrom-Json
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $($state.pid)"
    if ($process) {
        $expected = Join-Path $repo 'src/LifeOs.Api/bin/Debug/net10.0/LifeOs.Api.dll'
        if ($process.ExecutablePath -ne (Join-Path $repo '.tools/dotnet/dotnet.exe') -or !$process.CommandLine.Contains($expected)) {
            throw 'PID belongs to a different process; refusing to stop it.'
        }
        Stop-Process -Id $state.pid
    }
    Remove-Item -LiteralPath $statePath
}
$previous = $env:LIFEOS_DEV_DB_PASSWORD
try {
    $env:LIFEOS_DEV_DB_PASSWORD = (Get-Content (Join-Path $repo '.tools/dev/database.json') -Raw | ConvertFrom-Json).password
    docker compose -f (Join-Path $repo 'compose.dev.yaml') stop
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop Dev database.' }
} finally { $env:LIFEOS_DEV_DB_PASSWORD = $previous }
Write-Output 'LifeOS Dev stopped. Database and recordings are preserved.'
