param(
    [ValidateSet('Api', 'Worker', 'Migrate', 'Test')]
    [string]$Target = 'Api',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $projectRoot '.env.local'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw 'Crie .env.local conforme README antes de executar este script.'
}
$localSettings = @{}
foreach ($line in [System.IO.File]::ReadAllLines($settingsPath)) {
    if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
    $parts = $line -split '=', 2
    if ($parts.Count -ne 2 -or $parts[0] -notin @('ConnectionStrings__Engine', 'TEST_POSTGRES', 'Authentication__Authority', 'Authentication__Audience')) {
        throw 'Entrada de configuração local inválida.'
    }
    $localSettings[$parts[0]] = $parts[1]
}
$previousValues = @{}
foreach ($entry in $localSettings.GetEnumerator()) {
    $previousValues[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
Push-Location $projectRoot
$buildArguments = @()
if ($NoBuild) { $buildArguments = @('--no-build') }
$commandExitCode = 1
try {
    switch ($Target) {
        'Api' {
            dotnet run @buildArguments --project backend/Achadinhos.Api -- --urls http://localhost:8080 --environment Development --LocalDevelopment:Enabled=true --Simulation:Enabled=true
        }
        'Worker' {
            dotnet run @buildArguments --project backend/Achadinhos.Worker -- --environment Development
        }
        'Migrate' {
            dotnet tool restore
            if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar ferramenta EF.' }
            dotnet ef database update --project backend/Achadinhos.Infrastructure --startup-project backend/Achadinhos.Api
        }
        'Test' {
            dotnet test Achadinhos.sln --logger trx
        }
    }
    $commandExitCode = $LASTEXITCODE
} finally {
    Pop-Location
    foreach ($entry in $previousValues.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
if ($commandExitCode -ne 0) { throw "Comando falhou (código $commandExitCode)." }