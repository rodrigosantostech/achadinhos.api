# Planejamento de integração Telegram — Achadinhos Engine

Data: 23/10/2026.
Estado: contratos, cliente, persistência, Worker/Hangfire e endpoint de homologação implementados. Teste real bloqueado por HTTP 403; publicação comercial e produção pendentes.
Base: [guia original](TELEGRAM_INTEGRATION_AGENT_GUIDE.md), copiado sem alterações.
Este documento detalha a Fase 4 do [plano geral](../IMPLEMENTATION_PLAN.md) e considera a antecipação do fluxo manual descrita em [LOCAL_WORKFLOW.md](../LOCAL_WORKFLOW.md).

## 1. Diagnóstico do repositório

| Área | Evidência no código | Lacuna para Telegram |
| --- | --- | --- |
| Camadas | Solution com Api, Worker, Domain, Application, Infrastructure e Contracts | Reutilizar os projetos; nenhum serviço novo |
| Oferta | Domain/Discovery/Entities.cs: Offer, PriceSnapshot e Marketplace | Não existe link afiliado validado, TTL de publicação ou disponibilidade confirmada |
| Revisão e fila | Api/WorkspaceEndpoints.cs: cadastro, revisão, agendamento, consulta e cancelamento transacionais | Agendamento muda Offer.Status para Scheduled e cria Publication; não envia mensagens |
| Publicação | Domain/Discovery/WorkspaceEntities.cs: Publication com ID, oferta, status Scheduled, horário e operador | Faltam canal, snapshot, tentativas, lease e IDs Telegram |
| Banco | Infrastructure/EngineDbContext.cs e migrations InitialFoundation/LocalWorkspace | Índice único atual só cobre OfferId quando Status = Scheduled; não garante exclusão durante processamento |
| Worker | Worker/Program.cs: FoundationWorker com heartbeat e readiness do banco | Sem Hangfire, dispatcher ou executor de envio |
| Autorização | Api/Program.cs: policies Read, Write e Admin; LocalDevelopmentAccess.cs | Modo local pode atribuir Admin sem JWT; envio externo exige proteção adicional |
| Testes | WorkspaceTests.cs, ApiTests.cs e PostgresTests.cs | Já há banco real via TEST_POSTGRES e schema isolado; faltam testes de publisher e entrega |
| Configuração | scripts/dev.ps1, infra/compose.yaml e projetos startup | Sem configuração Telegram/UserSecretsId; script só aceita chaves de banco e autenticação |

Não há adapter Telegram nem SDK instalado. ProductUrl aceita HTTP no cadastro e não comprova afiliação. OriginalPrice informado manualmente não comprova histórico de desconto. A fila retorna deliveryEnabled=false.

O diagnóstico acima registra a situação inicial. T1/T2 foram implementados posteriormente; evidências e limites em [VALIDATION.md](../VALIDATION.md) e [TELEGRAM_CHECKLIST.md](TELEGRAM_CHECKLIST.md).

## 2. Escopo e decisões

MVP: oferta revisada, com dados válidos e link afiliado autorizado, gera postagem em canal privado de homologação pelo Worker; resposta e tentativas ficam persistidas. Primeiro validar envio textual; depois acrescentar imagem e fallback seguro.

O caminho técnico pode ser desenvolvido com fixtures e links HTTPS controlados antes do acesso Shopee. Esse teste deve identificar a oferta como fictícia e não comprova geração de afiliado nem homologação comercial.

- Reutilizar Publication e a tabela publications; não criar uma segunda publication_queue com a mesma responsabilidade.
- Application define contratos e orquestração; Infrastructure concentra HTTP e persistência; Worker executa envio.
- Manter Scheduled como estado inicial público, equivalente a Pending no guia. Evitar renomeação imediata que quebre API/UI e registros existentes.
- Novos estados de Publication: Processing, Published, RetryScheduled, Failed, UnknownDelivery e Cancelled. Estado da oferta e estado da entrega serão tratados separadamente.
- Endpoint administrativo apenas cria trabalho e retorna 202 com publicationId. Consultar resultado por ID. A exigência do guia de retornar IDs Telegram será atendida na consulta após execução, sem envio síncrono pela API.
- Reutilizar policy Admin, acrescentando exigência de identidade JWT válida para as rotas Telegram; a identidade LocalDevelopment não deve autorizar publicação externa. Login do painel continua adiado.
- Publicação desabilitada por padrão. Destino escolhido em configuração/registro autorizado, nunca por chatId arbitrário no request.
- Não prometer entrega exatamente uma vez: timeout, queda do processo e falha ao persistir sucesso exigem conciliação conservadora.

## 3. Incrementos e arquivos

Caminhos abaixo são relativos a backend/, salvo indicação. Nomes novos são propostas, não arquivos já implementados.

| Etapa | Arquivos a criar/editar | Entrega e aceite |
| --- | --- | --- |
| T1 — Contratos e configuração | Application/Publishing/ITelegramPublisher.cs, TelegramPublicationRequest.cs e TelegramPublicationResult.cs; Infrastructure/Telegram/TelegramOptions.cs e registro DI; Worker/Program.cs | DTO sem EF/Shopee, resultado com ChatId/MessageId/SentAt; opções verificadas quando habilitadas; token nunca exposto |
| T2 — Formatação e HTTP | Infrastructure/Telegram/TelegramMessageFormatter.cs, TelegramPublisher.cs e TelegramApiModels.cs; Infrastructure.csproj | Typed HttpClient via IHttpClientFactory e System.Text.Json; payloads distintos para texto/foto; erros classificados e logs sanitizados |
| T3 — Modelo e migrations | Domain/Discovery/WorkspaceEntities.cs; Domain/Publishing/PublicationChannel.cs, PublicationAttempt.cs e PublicationStatus.cs; Infrastructure/EngineDbContext.cs e Migrations/* | Evolução aditiva da publicação, índices, snapshot e tentativas; SQL revisado e migração de registros Scheduled segura |
| T4 — Agendamento e preflight | Application/Publishing/SchedulePublication.cs e PublicationPreflight.cs; Infrastructure/Publishing/PublicationRepository.cs; Api/WorkspaceEndpoints.cs; Contracts/WorkspaceContracts.cs | Reserva de destino e snapshot validado na transação; gates de origem, preço, revisão, disponibilidade, frescor e URL |
| T5 — Execução | Worker/Jobs/TelegramPublicationJob.cs, DispatchPublicationsJob.cs e PublicationMaintenanceJob.cs; Worker/Program.cs; projetos/lockfiles envolvidos | Hangfire com PostgreSQL, servidor só no Worker; job recebe apenas publicationId; claim concorrente, recuperação de fila e política de falhas |
| T6 — Homologação administrativa | Api/AdminTelegramEndpoints.cs e Program.cs; Contracts/TelegramContracts.cs; configuração dos hosts | POST /api/admin/telegram/test enfileira para canal de teste; consulta de resultado, auditoria, limitação de chamadas e autenticação JWT Admin |
| T7 — Operação | README.md, docs/VALIDATION.md, docs/integrations/TELEGRAM_CHECKLIST.md; scripts/dev.ps1 e infra/compose.yaml quando necessário | Instruções dos dois hosts, métricas, kill switch, conciliação, evidências reais e checklist de produção |

T2 e T3 dependem dos contratos de T1. T4 depende de T3; T5 integra T2–T4; T6 depende de T5. Preparação humana pode ocorrer durante desenvolvimento. T7 fecha a homologação.

Dependências previstas: Microsoft.Extensions.Http onde necessário; Hangfire e storage PostgreSQL compatíveis com .NET 10 e Npgsql. Verificar versões e manutenção antes de instalar e atualizar lockfiles. Não adicionar SDK Telegram nem Testcontainers por padrão: os testes relacionais existentes já usam PostgreSQL real, sem Docker obrigatório.

Após cada incremento de código, executar build e testes relevantes; registrar comandos e resultados reais, incluindo falhas e testes ignorados.

## 4. Persistência e compatibilidade

Adicionar a Publication: ChannelId, CampaignId opcional, IdempotencyKey, PayloadSnapshot jsonb versionado, PublishedAt, TelegramChatId bigint, TelegramMessageId integer, AttemptCount, LastErrorCode, LastErrorAt, LastAttemptAt, LeaseUntil, LeaseToken e UpdatedAt.

PublicationChannel registra ambiente e destino autorizado. Snapshot preserva título, moeda, preço, origem/data da observação, evidência de referência, URL afiliada validada/validade, imagem autorizada e disclosure. Não copiar credenciais ou payload bruto desnecessário.

PublicationAttempt registra número, início/fim, resultado, erro sanitizado, HTTP status, código Telegram, duração e correlação. Unique por publicação/número da tentativa.

Índices:
- Status/ScheduledAt para despacho e Status/LeaseUntil para manutenção.
- IdempotencyKey única e obrigatória para novos agendamentos.
- Unique parcial por oferta/canal enquanto Scheduled, Processing ou RetryScheduled, impedindo duas entregas ativas.
- Definir deduplicação por campanha explicitamente, com índices separados para campanha nula e não nula, se adotada. Chave do request e deduplicação comercial têm funções diferentes.

Aplicar migration aditiva, sem reescrever migrations aplicadas. Registros antigos ficam sem destino/snapshot e bloqueados para envio até revisão explícita; não associar automaticamente toda a fila existente ao novo canal.

Testar upgrade a partir de LocalWorkspace com registros Scheduled/Cancelled, preservação dos dados e SQL de downgrade em banco descartável. Downgrade após entregas reais pode perder auditoria e IDs; preferir rollback da aplicação com envio desligado e schema compatível.

A consulta da fila passa a informar a configuração efetiva e campos de entrega, substituindo o false fixo. Validar o contrato consumido pelo repositório UI antes de expor novos estados; mudança de telas é etapa posterior.

## 5. Execução, concorrência e falhas

1. Agendamento transacional grava Publication pronta para despacho; um dispatcher recupera registros elegíveis mesmo se houver falha entre commit e criação do job Hangfire.
2. Worker verifica kill switch, ambiente, data e estado; faz update condicional para Processing com lease/token. Só quem obteve o claim prossegue.
3. Revalidar snapshot, oferta, canal e validade do link imediatamente antes do envio. Aprovação manual não transforma disponibilidade Unknown em disponibilidade confirmada. Aplicar TTL conservador quando não houver consulta autorizada em tempo real.
4. Persistir tentativa antes de chamar o publisher. Não manter transação de banco aberta durante HTTP.
5. Persistir sucesso e IDs reais condicionado ao lease/token. Worker antigo não pode sobrescrever estado de outro executor.
6. Lease expirado após possível envio resulta em UnknownDelivery; token de lease protege o banco, mas não impede duplicação na API externa. Outro worker não deve retomar automaticamente uma tentativa possivelmente enviada.
7. Reconciliar UnknownDelivery por ação administrativa auditada: registrar mensagem localizada e IDs verificados, ou autorizar nova publicação explicitamente após revisão.

| Situação | Política |
| --- | --- |
| Rejeição confirmada por dados/permissões | Failed; corrigir causa antes de nova ação |
| 429 confirmado | RetryScheduled, respeitando retry_after e limite de tentativas |
| Falha transitória comprovadamente anterior ao envio | Backoff limitado com jitter |
| Timeout, conexão interrompida, 5xx/JSON inválido sem confirmação confiável | UnknownDelivery; sem retry/fallback automático |
| Telegram confirma sucesso, banco falha ao salvar | Preservar tentativa e sinalizar conciliação; nunca reenviar cegamente |
| Imagem rejeitada com certeza de ausência de publicação | Fallback textual controlado; registrar a tentativa |

Desabilitar retry automático do método de envio e qualquer retry HTTP genérico de POST. Reexecução do job deve consultar o estado antes de agir. Coordenação de rate limit por canal precisa funcionar entre instâncias; concorrência local não é suficiente.

## 6. HTTP, segurança e testes

Construir texto com dados conhecidos, preço pt-BR, HTML escapado e divulgação de afiliação. Testar limites de texto/caption, Unicode e truncamento de campos antes da montagem do HTML. Validar URL HTTPS sem credenciais; origem afiliada deve ser permitida e comprovada. Foto requer autorização de uso.

O token integra a rota HTTP do Telegram. Sanitizar logs de HttpClient, exceções e spans; não registrar URI autenticada, corpo bruto de erro ou secrets. O formatter não decide aprovação ou estratégia comercial.

Arquivos de teste propostos em tests/Achadinhos.Tests/:
- TelegramMessageFormatterTests.cs: escape, moeda, referência opcional, limites e ausência de descontos inventados.
- TelegramPublisherTests.cs: HttpMessageHandler controlado; texto/foto, leitura de IDs, 400/401/403/429, 5xx, timeout, resposta inválida e fallback.
- PublicationJobTests.cs: transições, TTL, cancelamento concorrente, kill switch, queda após envio e falha no commit.
- PublicationPersistenceTests.cs: padrão PostgresFact existente, dois DbContexts/claims concorrentes, idempotência, índices, migration e tentativas.
- AdminTelegramTests.cs: 401 anônimo, 403 Reviewer/Viewer/identidade local, Admin JWT permitido, destino fixo e restrição por ambiente.
- Verificar logs capturados usando token fictício para assegurar ausência da credencial e da URL autenticada.

Comandos de validação futuros, a partir da raiz API:

~~~powershell
dotnet restore Achadinhos.sln --locked-mode
dotnet build Achadinhos.sln --no-restore
dotnet format Achadinhos.sln --verify-no-changes --no-restore
dotnet test Achadinhos.sln --no-build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Test
~~~

O último comando inclui banco real conforme configuração local. Testes ignorados sem TEST_POSTGRES não comprovam concorrência. Estes comandos não foram executados nesta tarefa de documentação.

## 7. Preparação humana e secrets

1. Criar bot no [BotFather](https://t.me/BotFather); manter token fora de chat, Git, capturas de tela e logs.
2. Criar canal privado de homologação e adicionar bot administrador com permissão de publicar.
3. Identificar o destino real: canal público usa username; privado exige ID obtido por meio oficial. getUpdates requer ausência de webhook ativo. Não inventar ID nem remover webhook de outro sistema sem avaliar o impacto.
4. Configurar secrets no Worker, que efetivamente publica. Inicializar User Secrets no projeto e garantir seu carregamento em Development. Se API executar diagnóstico de conectividade, configurar também seu próprio acesso ao secret; secrets de um projeto não aparecem automaticamente no outro.
5. Validar getMe e mensagem de conectividade no ambiente local; guardar apenas resultado sanitizado e IDs de teste. O envio manual do guia é diagnóstico isolado; publicação da aplicação continua exclusiva do Worker.

Comandos de preparação sem valores sensíveis:

~~~powershell
dotnet user-secrets init --project backend/Achadinhos.Worker
~~~

Na implementação, orientar preenchimento local de Telegram:BotToken e Telegram:ChatId por entrada protegida, sem colar token no chat ou em comandos literais salvos no histórico. User Secrets é recurso de desenvolvimento, sem criptografia; produção usará secret manager/variáveis injetadas Telegram__BotToken e Telegram__ChatId.

Propor Telegram:Enabled=false e Telegram:TestEndpointEnabled=false por padrão, além de ambiente/destino de homologação configurados. scripts/dev.ps1 hoje rejeita novas chaves em .env.local; não adicionar token nesse arquivo esperando carregamento automático. Preferir secrets por host e só adaptar o script quando necessário.

Atualização: o responsável confirmou bot e canal criados. Configuração de secrets, permissões e conectividade ainda precisam de verificação. Essas etapas não bloquearam os contratos e testes locais.

## 8. Aceite e liberação

- [x] Guia incorporado e diagnóstico baseado no repositório.
- [x] Plano incremental, arquivos, dependências, migrations e testes definidos.
- [x] Contratos/publisher implementados e validados localmente (T1/T2; fallback após tentativa depende do job).
- [x] Migração e claim concorrente verificados em PostgreSQL real.
- [x] Retry seguro, entrega incerta e recuperação exercitados; cancelamento da fila comum preservado.
- [x] Endpoint administrativo protegido, sem uso da identidade local.
- [ ] Bot, secrets e canal de testes configurados pelo responsável.
- [ ] Oferta fictícia publicada pelo Worker com botão e IDs reais persistidos.
- [ ] Link afiliado autorizado, frescor e política comercial homologados.
- [ ] Operação documentada: métricas de fila/sucesso/erro/429/entrega incerta, alertas e kill switch.
- [ ] Produção: canal separado, credenciais isoladas, backups, configuração de identidade e aprovação humana registrada.

Riscos principais: duplicação após entrega incerta; publicação da fila antiga sem revisão; bypass local em rotas que enviam mensagens; credencial em telemetria; preço/link vencido; mudança de estados consumidos pela UI. Os gates acima devem ser comprovados antes de habilitar envio real.

Contratos, persistência e execução de homologação entregues com configuração desligada. Próximo gate: conexão real e primeira postagem; preflight comercial continua pendente. O acesso Shopee não bloqueia o adapter Telegram, mas bloqueia afirmar integração comercial completa.

## 9. Referências verificadas

Consulta em 23/10/2026; versões de dependências devem ser revalidadas na implementação.

- [Telegram Bot API](https://core.telegram.org/bots/api): contratos de envio, resposta e erros.
- [ASP.NET Core User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0): configuração por projeto e limites do armazenamento de desenvolvimento.
- [Hangfire: tratamento de exceções](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html): retry automático e desativação por job.

## Atualização T1/T2

Bot e canal já criados, conforme confirmação do responsável. Secrets e conectividade ainda pendentes.
39 casos novos Telegram e suíte total com 73 testes aprovados, incluindo PostgreSQL existente.
O cliente retorna ImageRejected sem reenviar: fallback após tentativa será feito pelo job e registrado em persistência.
Não há execução da fila nem postagem real neste incremento.
## Atualização de fila e homologação

Persistência, Worker/Hangfire, diagnóstico e endpoint de teste entregues para o caminho IsTest em canal privado Development.
91 testes aprovados com PostgreSQL real; migration aplicada na aplicação e runtime Hangfire validado com envio desligado.
O fluxo de teste/habilitação foi executado, mas parou em getMe: HTTP 403, resposta HTML.
O responsável reproduziu a falha; probes sem token também foram bloqueados.
Nenhum envio real, destino verificado ou teste fictício foi criado no banco local.

T4 comercial, conciliação administrativa completa, métricas/alertas e produção continuam pendentes.
As referências anteriores a T3/T5 como próximos passos registram a ordem original; o próximo gate atual é
liberar acesso HTTPS à Bot API e executar scripts/telegram.ps1 -Action Test.
Detalhes e evidências em TELEGRAM_CHECKLIST.md e ../VALIDATION.md.