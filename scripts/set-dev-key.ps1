$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$secret = Read-Host 'Klucz API AI dla LifeOS Dev (nie wysylaj go na czat)' -AsSecureString
try {
    $value = [Net.NetworkCredential]::new('', $secret).Password
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Klucz nie moze byc pusty.' }
    @{ 'Llm:ApiKey' = $value } | ConvertTo-Json -Compress | & (Join-Path $repo '.tools/dotnet/dotnet.exe') user-secrets set --id lifeos-local-dev
    if ($LASTEXITCODE -ne 0) { throw 'Nie udalo sie zapisac klucza.' }
    Write-Output 'Zapisano osobny klucz Dev poza repozytorium i APK. Uruchom ponownie Dev: stop-dev.ps1, start-dev.ps1.'
} finally { $value = $null; $secret.Dispose() }
