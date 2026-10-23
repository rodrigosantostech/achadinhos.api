# Achadinhos Engine — Guia de Integração Telegram para Agente de Código

> **Objetivo deste documento:** entregue este arquivo ao seu agente de desenvolvimento (Codex, Claude Code, Cursor ou equivalente). Ele deverá **inspecionar o repositório existente**, implementar a integração Telegram no backend .NET e **guiar o responsável passo a passo também nas tarefas externas ao código**, sem inventar credenciais nem afirmar que ações externas foram realizadas.
>
> **Escopo:** canal Telegram para ofertas da Shopee e futuros marketplaces; painel Next.js é secundário. **Não recriar a solução** nem introduzir microsserviços desnecessários.

## 0. Instrução principal para o agente

Leia integralmente este arquivo e depois execute o seguinte protocolo:

1. Inspecione o repositório: solution/projetos `.NET`, DI, padrões Application/Infrastructure, entidades `Offer`/`Publication`, jobs Hangfire, persistência EF Core, autenticação, testes e infraestrutura. Antes de editar, escreva uma **avaliação curta** do que já existe e do que falta. **Não assuma que o layout sugerido aqui corresponde ao projeto.**
2. Faça um plano incremental com arquivos a editar/criar, dependências, migrações, testes e riscos. Prefira adaptar estruturas existentes em vez de duplicá-las.
3. Identifique as etapas humanas externas (BotFather, criação de canal, permissões, secrets) e apresente instruções exatas. **Pare e peça somente o dado ou ação estritamente necessário para prosseguir**. Nunca peça que o token seja colado no chat; oriente o uso de User Secrets ou secret manager.
4. Implemente em pequenos incrementos compiláveis. Depois de cada incremento, execute build e testes disponíveis; reporte os comandos e resultados reais, inclusive falhas.
5. Não simule sucesso. Separe claramente `Implementado`, `Validado localmente`, `Pendente de credencial`, `Pendente de teste no canal` e `Pendente de produção`.
6. Ao final, atualize o README do repositório e disponibilize checklist de homologação e operação. Sugira a próxima melhoria apenas após fechar as condições de aceite atuais.

**Critérios não negociáveis:** segurança do token, autorização no endpoint de teste, logs sem vazamento de credenciais, persistência do `message_id`, publicação somente por worker, idempotência/concorrência, tratamento de entrega incerta, rate limit e conformidade com políticas de afiliados.

## 1. Contexto e resultado esperado

O Achadinhos Engine coleta ofertas por integrações autorizadas (inicialmente Shopee), normaliza produtos, salva preços, avalia e aprova ofertas, gera links de afiliados e as publica em um **canal do Telegram**. O backend já está parcialmente pronto:

`Marketplace Discovery → Offer Evaluation → Approval → Publication Queue → TelegramPublicationJob → Telegram Bot API → Publication persistence`

**MVP aceito:** uma oferta aprovada e válida produz uma postagem real no canal de homologação, com texto, preço e botão que abre o link de afiliado; o backend persiste `chat_id`, `message_id`, status e horários; erros têm tratamento explícito. Produção exige aprovação humana e validações adicionais.

## 2. Passo a passo externo ao código (orientar o usuário)

### 2.1 Criar bot

1. No aplicativo Telegram, abrir [@BotFather](https://t.me/BotFather) e conferir que é o bot oficial.
2. Enviar `/newbot`, escolher nome de exibição e username exclusivo terminado em `bot`.
3. Receber token. **Não publicar o token, não incluí-lo em arquivos versionados, logs, screenshots ou mensagens ao agente.**
4. Opcional: personalizar descrição, imagem e comandos do bot no BotFather.

**Evidência esperada:** usuário confirma que o bot foi criado. O agente orienta a verificar via `getMe`, sem receber o token.

### 2.2 Criar canal de homologação

1. Criar um **canal privado de testes** (não confundir canal com grupo).
2. Abrir administração do canal e adicionar o bot como administrador com permissão **Publicar mensagens** (`can_post_messages`).
3. Para canal público com username, `chat_id` pode ser `@nome_do_canal`.
4. Para canal privado, obter `chat.id` numérico por atualização `channel_post` recebida pelo bot (com `getUpdates`, desde que não exista webhook ativo) ou por outro método oficial autorizado; o valor costuma começar com `-100`, mas **não deve ser inventado**.
5. Manter canal de produção separado e não usar seu ID nos testes iniciais.

**Evidência esperada:** bot é administrador do canal, canal identificado corretamente, teste de envio concluído.

### 2.3 Configurar secrets e validar conectividade

No projeto startup da API:

```bash
dotnet user-secrets init
dotnet user-secrets set "Telegram:BotToken" "TOKEN_RECEBIDO_NO_BOTFATHER"
dotnet user-secrets set "Telegram:ChatId" "@canal_publico_ou_id_numerico"
```

Atenção: os comandos acima são executados **pelo usuário em seu ambiente**, com valores reais. O agente não deve solicitar nem registrar o token em sua conversa.

Configuração pública (sem token) no `appsettings.json`:

```json
{
  "Telegram": {
    "ChatId": "@canal_publico_ou_id_numerico"
  }
}
```

Produção: fornecer `Telegram__BotToken` e `Telegram__ChatId` por variáveis de ambiente/secret manager; separar configuração de homologação e produção. Validar `getMe` e `sendMessage` antes de conectar o Hangfire. Nunca registrar a URL completa `https://api.telegram.org/bot<TOKEN>/...`.

**Validação de conectividade (PowerShell, executar localmente):**

```powershell
$env:TELEGRAM_BOT_TOKEN = Read-Host "Token do bot"
$env:TELEGRAM_CHAT_ID = "@seu_canal_de_teste"
Invoke-RestMethod -Uri "https://api.telegram.org/bot$($env:TELEGRAM_BOT_TOKEN)/getMe"
$body = @{ chat_id = $env:TELEGRAM_CHAT_ID; text = "Achadinhos: canal de teste conectado" }
Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$($env:TELEGRAM_BOT_TOKEN)/sendMessage" -Body ($body | ConvertTo-Json) -ContentType "application/json"
```

Após os testes, limpar variáveis sensíveis da sessão se apropriado. O uso de variáveis não impede que a URL de solicitação apareça em traces/proxies; proteger logs e ferramentas de diagnóstico.

**Erros frequentes:** `401 Unauthorized` token inválido; `400 chat not found` ID/username incorreto ou bot sem acesso; `403` ou falta de direitos por configuração de administrador; `429` esperar `parameters.retry_after`, respeitando limites da API.

## 3. Contratos e responsabilidades

Manter abstração na camada Application, implementação em Infrastructure. Adaptar nomenclatura ao projeto existente.

```csharp
public sealed record TelegramPublicationRequest(
    string Title,
    decimal CurrentPrice,
    decimal? ReferencePrice,
    string AffiliateUrl,
    string? ImageUrl,
    string? DisclosureText = "Publicidade: link de afiliado.");

public sealed record TelegramPublicationResult(
    long ChatId,
    int MessageId,
    DateTimeOffset SentAt);

public interface ITelegramPublisher
{
    Task<TelegramPublicationResult> PublishAsync(
        TelegramPublicationRequest request,
        CancellationToken cancellationToken = default);
}
```

- Um DTO independente de entidades EF Core e de classes da Shopee.
- O publisher só envia; **não decide** score, aprovação nem estratégia comercial.
- O orquestrador/job é responsável por validar estado, reservar trabalho e persistir resultado.
- Nunca retornar o token da API em endpoints ou logs.
- Valores monetários são obtidos de snapshots validados, com moeda e origem conhecidas. Não inventar preço anterior, porcentagem ou escassez.

## 4. Cliente Bot API e formatação

Preferência para MVP: `IHttpClientFactory`/typed `HttpClient` e `System.Text.Json`. Não é obrigatório instalar SDK externo. API base: `https://api.telegram.org/bot{token}/`; **redigir URI antes de logging**.

### Endpoints oficiais

| Método | Uso |
| --- | --- |
| `getMe` | Validar token |
| `getChat` | Inspecionar chat conhecido |
| `sendMessage` | Texto com `reply_markup` |
| `sendPhoto` | Imagem + caption + botão |
| `editMessageText` | Alterar mensagem textual |
| `editMessageCaption` | Alterar legenda de foto |
| `deleteMessage` | Remover mensagem dentro das regras da API |
| `getUpdates` | Opcional, somente diagnóstico ou processamento de updates; não necessário para publicar |

Documentação oficial: https://core.telegram.org/bots/api e https://core.telegram.org/bots/features#botfather.

### Payload JSON para envio textual

```json
{
  "chat_id": "@canal_de_teste",
  "text": "<b>SSD NVMe 1 TB</b>\n<b>Por R$ 329,90</b>\nPublicidade: link de afiliado.",
  "parse_mode": "HTML",
  "reply_markup": {
    "inline_keyboard": [[{ "text": "Ver oferta", "url": "https://example.com/offer" }]]
  }
}
```

O URL de exemplo não é um link de afiliado real; substituí-lo somente após geração e validação do link pelo conector correto.

Para `sendPhoto`, usar `photo` e `caption` no lugar de `text`. **Não mandar propriedades inválidas/incompatíveis para o endpoint**: construir DTOs separados de envio para texto e foto. Preferir foto remota autorizada ou upload multipart quando necessário, verificando restrições do Telegram e do provedor da imagem. Fazer fallback para texto em caso de imagem indisponível ou rejeitada, tomando cuidado para não duplicar envio em caso de resposta incerta.

### Texto e limites

- Usar `parse_mode=HTML` e escapar campos externos com `WebUtility.HtmlEncode`.
- Texto em `sendMessage`: máximo de 4096 caracteres após parsing de entidades.
- Caption de `sendPhoto`: máximo de 1024 caracteres após parsing de entidades.
- Truncar **somente campos controlados**, com testes para HTML válido; não cortar strings HTML arbitrariamente.
- Incluir transparência publicitária sobre link de afiliado.
- Não afirmar que houve desconto real se histórico não o comprova.

## 5. Camadas sugeridas sem impor novo layout

```text
Application/
  Publishing/ITelegramPublisher.cs
  Publishing/SchedulePublication.cs
Infrastructure/
  Telegram/TelegramOptions.cs
  Telegram/TelegramPublisher.cs
  Telegram/TelegramApiModels.cs
  Persistence/PublicationRepository.cs
Worker/
  Jobs/TelegramPublicationJob.cs
Api/
  Endpoints/AdminTelegramTestEndpoint.cs
Tests/
  Unit/TelegramMessageFormatterTests.cs
  Integration/TelegramPublisherTests.cs
  Integration/PublicationJobTests.cs
```

Se o repositório já tiver módulos e nomenclaturas, **use os existentes**; não duplicar `Publication` e `TelegramPublisher` sob novas pastas desnecessariamente.

## 6. Fluxo transacional de publicação

1. O serviço de domínio aprova oferta após validação de preço, disponibilidade, vendedor, prazo do preço e link afiliado.
2. `publication_queue` recebe registro `Pending` com `offer_id`, `channel_id`, `scheduled_at`, snapshot e chave idempotente.
3. Hangfire dispara o job com **somente o ID da publicação**.
4. Worker faz **claim atômico** (update condicional `Pending → Processing` com lease/fencing token ou mecanismo equivalente) para evitar dois workers enviando a mesma publicação.
5. Antes do envio: validar snapshot e link; quando atualização de preço em tempo real não for possível por API autorizada, aplicar TTL e/ou aprovação manual.
6. Construir payload tipado; enviar ao Telegram.
7. Se API confirma sucesso: persistir `telegram_chat_id`, `telegram_message_id`, `published_at` e estado `Published`.
8. Erro **comprovadamente sem envio**: marcar `Failed` ou `RetryScheduled`, aplicar backoff e limite de tentativas; para HTTP 429, respeitar `retry_after`.
9. Timeout, quebra de conexão ou erro após tentativa sem certeza de entrega: marcar `UnknownDelivery` e **não repetir automaticamente**. Sinalizar para conciliação/revisão humana. A Bot API não fornece uma chave genérica de idempotência para mensagens.
10. Um job de manutenção identifica leases expirados e publicações incertas; não converte automaticamente todo `Processing` expirado em `Pending`.

### Estados

`Pending`, `Processing`, `Published`, `RetryScheduled`, `Failed`, `UnknownDelivery`, `Cancelled`.

### Persistência mínima

- `publications`: id, offer_id, channel_id, campaign_id nullable, status, scheduled_at, published_at, `telegram_chat_id` (bigint), `telegram_message_id` (integer), `attempt_count`, `last_error_code`, `last_error_at`, `last_attempt_at`, `lease_until`, `created_at`, `updated_at`, payload_snapshot jsonb.
- `publication_attempts`: id, publication_id, attempt_number, started_at, finished_at, outcome, error_kind, http_status, telegram_error_code, duration_ms, correlation_id; **sem URL autenticada**.
- Índice por `status, scheduled_at`; índice único de deduplicação definido conforme regra de campanha/canal. Evitar índice com `NULL` que não deduplica como esperado.
- Migrar via EF Core, revisar SQL de migration e testar rollback/compatibilidade.

### Hangfire

- Não aplicar retry automático indiscriminado a operações de envio. Definir política específica e deixar envio incerto para reconciliação.
- Separar filas de publicação e descoberta, se o volume justificar.
- Garantir jobs independentes de `HttpContext`; usar DI padrão e `CancellationToken`.

## 7. Endpoint administrativo de teste

Pode ser `POST /api/admin/telegram/test`, com autenticação e policy `AdminOnly`, **desabilitado ou restrito por ambiente**. Recebe dados de teste validados, publica **somente no canal de homologação** e retorna chat/message IDs, sem token. Proteger contra abuso, adicionar auditoria e não permitir que o cliente selecione arbitrariamente destinos de produção.

O painel Next.js, se necessário, exibirá `Teste de conexão`, `Publicar teste` e estado da integração, mas o foco é o backend; não construir uma UI elaborada neste momento.

## 8. Testes exigidos

### Unitários

- Escapamento HTML com título contendo `<`, `&`, aspas e caracteres Unicode.
- Formatação de preço usando `pt-BR`, decimais e referência de preço opcional.
- Sem porcentagem ou preço antigo inventado.
- Limites de texto/caption e fallback de imagem.
- Validação de URL afiliada (somente HTTPS, rejeição de esquemas inseguros).
- Máquina de estados: transições permitidas e proibidas.

### Integração

- Mock de `HttpMessageHandler`: envio texto/foto e leitura correta de `result.chat.id` e `message_id`.
- Erros da Bot API: 400, 401, 403, 429 com `retry_after`, 5xx, timeout, JSON malformado.
- EF Core + PostgreSQL real (preferir Testcontainers): claim concorrente, deduplicação, migrações, persistência de tentativas e sucesso.
- Dois jobs concorrentes não publicam duas vezes uma mesma publicação.
- Teste manual no canal de homologação com oferta fictícia e link controlado.

### Critérios de aceite

- [ ] Bot criado e token protegido.
- [ ] Canal privado de testes com bot administrador.
- [ ] `getMe` válido e primeira mensagem manual publicada.
- [ ] `ITelegramPublisher` integrado ao DI e testado.
- [ ] Mensagem com texto, preço e botão enviado ao canal de testes.
- [ ] `chat_id` e `message_id` reais persistidos.
- [ ] Claim atômico e proteção contra concorrência verificados.
- [ ] Erros e rate limits tratados com segurança.
- [ ] Entregas incertas não são reenviadas sem revisão.
- [ ] Endpoint de homologação protegido.
- [ ] Testes automatizados e build executados com resultados registrados.
- [ ] Checklist de produção revisado antes de usar canal público.

## 9. Observabilidade, segurança e operação

- Logs com `publication_id`, `offer_id`, `channel_id`, `correlation_id`, `attempt_id`, status e latência, **sem token e sem dados sensíveis desnecessários**.
- OpenTelemetry para API/Worker; mascarar URL com token em spans, logs e exceções.
- Métricas: `telegram_publish_success_total`, `telegram_publish_failure_total`, `telegram_unknown_delivery_total`, `telegram_rate_limited_total`, tempo da API, tamanho da fila e atraso de agendamento.
- Alertas para falhas consecutivas, aumento de `UnknownDelivery` e canal indisponível.
- Se token for exposto, revogar/rotacionar via BotFather e atualizar secrets.
- Validar conformidade de imagens, uso de links, tracking e publicidade com as regras da Shopee e do Telegram; **não presumir permissão de scraping nem de mascaramento de links**.
- Separar ambiente de teste e produção; restringir RBAC; permitir desligar publicações por `feature flag`/kill switch.

## 10. Roadmap recomendado

| Etapa | Entrega concreta | Gate de qualidade |
|---|---|---|
| 0. Diagnóstico | Mapear estrutura existente e lacunas | Sem duplicação arquitetural |
| 1. Setup externo | Bot, token local, canal e permissões | `getMe` e envio manual bem-sucedidos |
| 2. Infra | DTOs, DI, HttpClient, publisher e formatter | Testes de payload e handling de erros |
| 3. Persistência | Migrations e repositório de publicações | Testcontainers / PostgreSQL |
| 4. Orquestração | Job Hangfire com claim e estados | Concorrência e falhas testadas |
| 5. Homologação | Endpoint protegido e publicação real no canal de testes | Evidências de mensagem + persistência |
| 6. Produção | Canal real, configs isoladas, métricas e kill switch | Checklist e aprovação humana |
| 7. Evolução | Editar oferta expirada, tracking e otimizar agenda | Só após MVP estável |

## 11. Sessão interativa que o agente deve conduzir

O agente deve usar esta sequência ao conversar com o desenvolvedor:

**A. Diagnóstico:** “Inspecionei os módulos X/Y/Z. Já existem A/B e faltam C/D. Recomendo reutilizar X e adicionar Y. Posso iniciar a etapa de contratos e infraestrutura.”

**B. Preparação externa:** “Crie o bot em @BotFather; armazene o token via User Secrets; crie canal privado; adicione o bot como administrador; informe apenas quando terminar, sem enviar o token.”

**C. Verificação:** “Execute os comandos de `getMe` e `sendMessage`. Confirme se o teste apareceu no canal; se não, compartilhe apenas o código de erro sanitizado.”

**D. Implementação:** “Implementei os arquivos listados; build/testes executados com resultado X; o próximo passo é validar a mensagem real de homologação.”

**E. Release:** “Os critérios de aceite estão X/Y concluídos. Produção requer configurar canal real, secrets, limites e estratégia para entregas incertas; estas etapas ainda dependem de ação humana.”

Evite pedir várias informações de uma vez quando o próximo passo puder ser realizado com os dados já presentes. Se o agente não conseguir executar ferramentas externas, forneça comandos verificáveis sem afirmar execução.

## 12. Sugestões futuras após primeira postagem estável

1. Atualização ou desativação de ofertas vencidas: `editMessageCaption` / `editMessageText`, observando limites e permissões.
2. Tracking opt-in de cliques com redirecionamento próprio **somente se permitido pelo afiliado**, com minimização de dados conforme LGPD.
3. Estratégia de cadência por categoria, repetição e relevância; não presumir que mais postagens aumentam conversões.
4. Templates de texto A/B com dados verificáveis; IA é opcional.
5. Painel de saúde dos canais e reconciliação de `UnknownDelivery`.

## 13. Referências para verificação durante o desenvolvimento

- BotFather: https://t.me/BotFather
- Telegram Bot API: https://core.telegram.org/bots/api
- Bots / criação: https://core.telegram.org/bots/features#botfather
- Envio de mensagens: https://core.telegram.org/bots/api#sendmessage
- Envio de fotos: https://core.telegram.org/bots/api#sendphoto
- Políticas e FAQ: https://core.telegram.org/bots/faq
- ASP.NET Core HttpClientFactory: https://learn.microsoft.com/aspnet/core/fundamentals/http-requests
- .NET User Secrets: https://learn.microsoft.com/aspnet/core/security/app-secrets
- Hangfire: https://docs.hangfire.io/

> **Instrução final ao agente:** comece **agora** pela inspeção do repositório, apresente diagnóstico e plano baseado no código encontrado, implemente o que for possível e guie as tarefas externas até a publicação no canal de homologação. Não introduza novas tecnologias sem necessidade, não peça tokens em chat e não declare o fluxo concluído antes de obter evidência dos testes.
