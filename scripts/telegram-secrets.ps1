# Run locally in a terminal. Never send the bot token to the coding agent.
$ErrorActionPreference = 'Stop'
$workerProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'backend\Achadinhos.Worker'
$secureToken = Read-Host 'Token do BotFather (entrada oculta)' -AsSecureString
$telegramToken = $null
$settings = $null
$settingsJson = $null
try {
    $telegramToken = [System.Net.NetworkCredential]::new('', $secureToken).Password
    $chatId = Read-Host 'ID numerico negativo do canal privado ou @username do canal publico'
    if ($telegramToken -notmatch '\A[0-9]{5,}:[A-Za-z0-9_-]{20,}\z') {
        throw 'Formato do token invalido. Confira localmente no BotFather; nao compartilhe o valor.'
    }
    $numericChatId = 0L
    $isChannelId = [long]::TryParse($chatId, [ref]$numericChatId) -and $numericChatId -lt 0
    if (-not $isChannelId -and $chatId -notmatch '\A@[A-Za-z][A-Za-z0-9_]{4,31}\z') {
        throw 'Destino invalido. Informe o ID real do canal ou seu @username publico.'
    }
    $settings = @{
        'Telegram:BotToken' = $telegramToken
        'Telegram:ChatId' = $chatId
        'Telegram:Enabled' = 'false'
        'Telegram:TimeoutSeconds' = '15'
    }
    # JSON via stdin keeps the token out of command-line arguments and terminal history.
    $settingsJson = $settings | ConvertTo-Json -Compress
    $settingsJson | dotnet user-secrets set --project $workerProject | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel salvar User Secrets do Worker.' }
    Write-Host 'Configuracao salva em User Secrets do Worker. Publicacao continua desligada.'
    Write-Host 'Token/canal ainda precisam de validacao de conectividade; nenhum envio foi realizado.'
} finally {
    if ($settings) { $settings.Clear() }
    $settingsJson = $null
    $telegramToken = $null
    $secureToken.Dispose()
}
