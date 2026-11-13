param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
$dev = Join-Path $repo '.tools/dev'
New-Item -ItemType Directory -Path $dev -Force | Out-Null
$settingsPath = Join-Path $dev 'database.json'
if (!(Test-Path $settingsPath)) {
    @{ password = [Guid]::NewGuid().ToString('N') } | ConvertTo-Json | Set-Content $settingsPath -Encoding utf8
}
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$names = @('LIFEOS_DEV_DB_PASSWORD', 'ConnectionStrings__DefaultConnection', 'ASPNETCORE_ENVIRONMENT', 'LifeOs__LocalDev')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:LIFEOS_DEV_DB_PASSWORD = $settings.password
    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=55439;Database=lifeos_dev;Username=lifeos_dev;Password=$($settings.password)"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:LifeOs__LocalDev = 'true'
    try { $running = Invoke-RestMethod 'http://127.0.0.1:5081/api/environment' -TimeoutSec 2 } catch { $running = $null }
    if ($running) {
        if ($running.name -ne 'Dev') { throw 'Port 5081 belongs to a different environment. Nothing was changed.' }
        Write-Output 'LifeOS Dev is already running: http://127.0.0.1:5081'
        return
    }
    docker compose -f (Join-Path $repo 'compose.dev.yaml') up -d --wait
    if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop and retry.' }
    if (!$NoBuild) {
        & $dotnet build (Join-Path $repo 'src/LifeOs.Api') --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    }
    $dll = Join-Path $repo 'src/LifeOs.Api/bin/Debug/net10.0/LifeOs.Api.dll'
    if (!(Test-Path $dll)) { throw 'Build the backend first.' }
    $process = Start-Process -FilePath $dotnet -ArgumentList @(('"'+$dll+'"')) -WorkingDirectory (Join-Path $repo 'src/LifeOs.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dev 'server.log') -RedirectStandardError (Join-Path $dev 'server-error.log')
    @{pid=$process.Id; executable=$dotnet; dll=$dll} | ConvertTo-Json | Set-Content (Join-Path $dev 'server.json')
    for ($attempt=0; $attempt -lt 45; $attempt++) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { throw 'Dev stopped during startup. Check .tools/dev/server-error.log and server.log.' }
        try { $state = Invoke-RestMethod 'http://127.0.0.1:5081/api/environment' -TimeoutSec 1 } catch { continue }
        if ($state.name -ne 'Dev') { throw 'Unexpected environment on port 5081.' }
        Write-Output 'LifeOS Dev ready: http://127.0.0.1:5081'
        if (!$state.aiConfigured) { Write-Output 'AI key not configured. Run scripts/set-dev-key.ps1, then stop-dev.ps1 and start-dev.ps1.' }
        return
    }
    throw 'Dev did not become ready. Check .tools/dev/server-error.log.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
