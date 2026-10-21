$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (!(Test-Path '.env')) {
 $text = Get-Content '.env.example' -Raw
 foreach ($name in @('DB_PASSWORD','ADMIN_PASSWORD','SYNC_KEY','BACKUP_STATUS_KEY')) {
  $bytes = New-Object byte[] 36
  $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  $rng.GetBytes($bytes); $rng.Dispose()
  $secret = [Convert]::ToBase64String($bytes).Replace('+','-').Replace('/','_').TrimEnd('=')
  $text = [regex]::Replace($text, "(?m)^$name=\r?`$", "$name=$secret")
 }
 [IO.File]::WriteAllText((Join-Path (Get-Location) '.env'), $text)
 Write-Host '.env criado. Usuário admin; consulte ADMIN_PASSWORD no arquivo local e proteja-o.'
}
docker compose up -d --build
if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar. Consulte docker compose logs.' }
Write-Host 'Inicialização solicitada. Aguarde /health responder antes de entrar. Consulte docs/instalacao-windows.md.'
