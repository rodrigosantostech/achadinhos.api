param(
    [ValidateSet('All', 'Api', 'Frontend')]
    [string]$Target = 'All'
)
$ErrorActionPreference = 'Stop'
$apiRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $apiRoot
$uiRoot = Join-Path $workspaceRoot 'UI'

if ($Target -eq 'Api') {
    $Host.UI.RawUI.WindowTitle = 'Achadinhos - API'
    & (Join-Path $PSScriptRoot 'dev.ps1') -Target Api -NoBuild
    exit
}
if ($Target -eq 'Frontend') {
    $Host.UI.RawUI.WindowTitle = 'Achadinhos - Frontend'
    Push-Location $uiRoot
    try {
        & npm.cmd start
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar o frontend.' }
    } finally { Pop-Location }
    exit
}

foreach ($command in @('dotnet', 'npm.cmd', 'powershell.exe')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Comando nao encontrado: $command. Instale os requisitos indicados nos READMEs."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $uiRoot 'package.json'))) {
    throw "Frontend nao encontrado em $uiRoot. Mantenha API e UI na mesma pasta."
}
if (-not (Test-Path -LiteralPath (Join-Path $apiRoot '.env.local'))) {
    throw 'Crie API/.env.local conforme API/README.md antes de iniciar.'
}
foreach ($port in @(8080, 3000)) {
    $listener = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $port }
    if ($listener) { throw "A porta $port esta ocupada. Encerre o servico anterior antes de iniciar novamente." }
}
Push-Location $apiRoot
try {
    Write-Host 'Compilando backend...'
    & dotnet build Achadinhos.sln
    if ($LASTEXITCODE -ne 0) { throw 'Build do backend falhou. Nenhum servico foi iniciado.' }
} finally { Pop-Location }
Push-Location $uiRoot
try {
    if (-not (Test-Path -LiteralPath 'node_modules')) {
        Write-Host 'Instalando dependencias do frontend...'
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao instalar dependencias do frontend.' }
    }
    Write-Host 'Compilando frontend...'
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Build do frontend falhou. Nenhum servico foi iniciado.' }
} finally { Pop-Location }
foreach ($service in @('Api', 'Frontend')) {
    Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath), '-Target', $service
    ) -WorkingDirectory $workspaceRoot
}
Write-Host 'Servicos iniciando em janelas separadas:'
Write-Host 'API: http://localhost:8080'
Write-Host 'Frontend: http://localhost:3000'
Write-Host 'Use Ctrl+C em cada janela para encerrar. O PostgreSQL precisa estar em execucao.'