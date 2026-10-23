param(
    [ValidateSet('Check','Migrate','Test','Worker','Status')]
    [string]$Action = 'Check',
    [string]$TestKey = 'first-private-channel-test-20261023',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workerProject = Join-Path $projectRoot 'backend\Achadinhos.Worker'
$workerDll = Join-Path $workerProject 'bin\Telegram\net10.0\Achadinhos.Worker.dll'
$settingsPath = Join-Path $projectRoot '.env.local'
$prior = @{}
Push-Location $projectRoot
try {
    if (Test-Path -LiteralPath $settingsPath) {
        foreach ($line in [System.IO.File]::ReadAllLines($settingsPath)) {
            $parts = $line -split '=', 2
            if ($parts.Count -eq 2 -and $parts[0] -eq 'ConnectionStrings__Engine') {
                $prior[$parts[0]] = [Environment]::GetEnvironmentVariable($parts[0], 'Process')
                [Environment]::SetEnvironmentVariable($parts[0], $parts[1], 'Process')
            }
        }
    }
    if (-not $NoBuild) {
        dotnet build $workerProject -c Telegram
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar Worker.' }
    }
    if (-not (Test-Path -LiteralPath $workerDll)) { throw 'Compile o Worker antes de usar NoBuild.' }
    switch ($Action) {
        'Check' {
            dotnet $workerDll --environment Development --TelegramAction=check
            if ($LASTEXITCODE -ne 0) { throw 'Diagnostico Telegram falhou. Nenhuma publicacao foi habilitada.' }
        }
        'Status' {
            dotnet $workerDll --environment Development --TelegramAction=status --Logging:LogLevel:Default=Warning
            if ($LASTEXITCODE -ne 0) { throw 'Consulta de estado falhou.' }
        }
        'Migrate' {
            dotnet $workerDll --environment Development --TelegramAction=migrate
            if ($LASTEXITCODE -ne 0) { throw 'Migration falhou.' }
        }
        'Test' {
            dotnet user-secrets set 'Telegram:Enabled' 'false' --project $workerProject | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel desligar envio antes da verificacao.' }
            # Same key intentionally prevents duplicate test messages on repeated execution.
            if ($TestKey.Length -lt 8 -or $TestKey.Length -gt 100) { throw 'TestKey deve ter 8 a 100 caracteres.' }
            dotnet $workerDll --environment Development --TelegramAction=check
            if ($LASTEXITCODE -ne 0) { throw 'Conexao nao validada; envio permanece desligado.' }
            dotnet $workerDll --environment Development --TelegramAction=prepare-test "--TelegramTestKey=$TestKey"
            if ($LASTEXITCODE -ne 0) { throw 'Canal privado ou fila nao validado; envio permanece desligado.' }
            dotnet $workerDll --environment Development --TelegramAction=run-test "--TelegramTestKey=$TestKey" --Telegram:Enabled=true
            if ($LASTEXITCODE -ne 0) { throw 'Teste nao confirmado. Consulte estado persistido antes de tentar novamente.' }
            dotnet user-secrets set 'Telegram:Enabled' 'true' --project $workerProject | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Teste passou, mas nao foi possivel persistir a habilitacao.' }
            Write-Host 'Mensagem de teste confirmada e envio de homologacao habilitado. Use -Action Worker para manter o executor ativo.'
        }
        'Worker' {
            dotnet $workerDll --environment Development
            if ($LASTEXITCODE -ne 0) { throw 'Worker encerrou com falha.' }
        }
    }
} finally {
    foreach ($entry in $prior.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    Pop-Location
}
