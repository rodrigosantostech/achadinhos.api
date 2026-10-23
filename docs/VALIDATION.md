# Validação da fase inicial

Atualização: 23/10/2026. Windows, SDK .NET 10.0.401.
Pasta: C:/Dev/projeto-teste/API.

## Executado com sucesso

- Restore com lockfiles; build Release com zero avisos/erros; formatação aprovada na implementação inicial.
- Migration InitialFoundation gerada e sem drift de modelo; SQL idempotente em infra/initial-migration.sql.
- PostgreSQL 18.6 local em localhost:5432, serviço postgresql-x64-18.
- Criados os bancos achadinhos e achadinhos_test; não foram alterados outros bancos ou usuários.
- Migration InitialFoundation aplicada em achadinhos.
- Testes: 15 aprovados, zero falhas e zero ignorados, incluindo PostgreSQL real em achadinhos_test.
- Teste relacional: migration, valores monetários, unique por marketplace/oferta e FK de snapshot; schema de teste isolado removido ao final.
- API iniciada como processo real: /health/live retornou live e /health/ready retornou ready.
- Worker iniciado como processo real: heartbeat DatabaseReady=True e DiscoveryEnabled=false.
- Os dois processos usados para validação foram encerrados ao final.
- Conexões em .env.local, confirmado como ignorado pelo Git; nenhuma senha incluída nos documentos.
- scripts/dev.ps1 permite migrar, executar API/Worker e rodar testes sem Docker.
- Git local inicializado, sem commit ou push.

Os testes HTTP usam host em memória, JWT assinado de teste e substituição de IOfferQueries.
A validação real dos hosts comprova inicialização, health checks e heartbeat conectados ao banco;
ainda não comprova autenticação com IdP externo ou coleta de ofertas.

## Pendências

| Item | Motivo / próximo passo |
| --- | --- |
| Imagens Docker, Compose e migration bundle Linux | Docker ausente; opcionais para desenvolvimento local neste momento |
| CI no GitHub | Workflow criado, execução remota ainda não realizada |
| IdP real | Configurar issuer, audience, cliente e roles; testar token real |
| Shopee | Contrato e credenciais aprovadas a validar; Fase 2 |
| Telegram e painel | Fases 4 e 5 |

PostgreSQL local substitui Docker no fluxo atual de desenvolvimento.
As validações de banco antes pendentes estão concluídas; a homologação do IdP continua aberta.
## Separação dos repositórios — 23/10/2026

- Backend e Git original movidos para C:/Dev/projeto-teste/API.
- Frontend criado em C:/Dev/projeto-teste/UI, com Git e CI próprios.
- Raiz do workspace sem .git; configuração de editor em achadinhos.code-workspace.
- Referências de projeto conferidas/restauradas após atualização automática do editor durante a movimentação.
- Caches antigos preservados em API/.local, ignorado pelo Git; caminhos antigos limpos da raiz.
- Após a mudança: build Release sem avisos/erros, formatação aprovada e 15 testes aprovados com PostgreSQL real.
- API iniciada no novo diretório e consumida pelo servidor Next.js por HTTP com sucesso.
- Conexão PostgreSQL continua exclusivamente em API/.env.local, ignorado pelo Git.
- Sem commits, remotes ou publicação externa.
## Fluxo local sem login — 23/10/2026

- 34 testes aprovados, zero ignorados, via scripts/dev.ps1 -Target Test.
- PostgreSQL real: migrations, integridade, cadastro HTTP, histórico, busca/filtro,
  rejeição/aprovação, datas inválidas, dois agendamentos concorrentes, cancelamento e reagendamento.
  Dados de teste confinados a schemas isolados em achadinhos_test, removidos ao terminar.
- Modo local testado: exige Development explícito + flag + loopback; recusa Origin, Host externo
  e Authorization; em Production continua 401.
- Migration 20261023185010_LocalWorkspace aplicada em achadinhos.
- API /health/ready retorna ready; ofertas e agendamentos vazios, sem dados artificiais.
- UI: lint, typecheck e build aprovados. Quatro telas verificadas por HTTP com status 200.
- Server Action real de cadastro verificada até a API e retorno de validação na tela, sem criar registros.
- Prévia desktop de Ofertas conferida com Edge headless.

Limites: não há login, coleta Shopee, geração de afiliados, score ou entrega Telegram.
Fila local persistida não executa envios. Não houve validação de integrações externas.

## Telegram T1/T2 — 23/10/2026

- Contratos Application, options validadas, registro DI no Worker, formatter e typed HttpClient implementados.
- Texto/foto com payloads separados; resultado lê IDs e data retornados pelo Telegram.
- Sem retry de envio; rejeição, rejeição de imagem, 429 e entrega incerta têm metadados estruturados.
- Tokens não são incluídos em erros; transporte sem logs genéricos, redirects, cookies ou Activities de requisição.
- Worker preparado para User Secrets em Development; script local com entrada oculta e gravação via stdin.
- Bot e canal existentes segundo confirmação do responsável, sem verificação externa nesta execução.

Validação executada:
- dotnet restore Achadinhos.sln: aprovado, lockfiles atualizados.
- Build padrão encontrou DLLs bloqueadas pela API em execução (MSB3026/MSB3027); a API foi mantida em funcionamento.
- dotnet build Achadinhos.sln --artifacts-path .local/telegram-validation: aprovado, zero avisos/erros.
- Primeira suíte: 69 aprovados, 2 falhas de tipo nos dados de testes xUnit novos e 2 ignorados sem TEST_POSTGRES.
  Corrigidos os dados opcionais para double.
- Suíte final com TEST_POSTGRES carregado sem exibir valores e saída isolada: 73 aprovados, zero falhas e zero ignorados.
  Inclui os dois testes PostgreSQL existentes e 39 novos casos Telegram, todos sem rede Telegram.
- Casos novos cobrem HTML/Unicode/limites, preços, URLs, texto/foto, leitura de IDs, erros Bot API,
  timeout/cancelamento, ausência de repetição, configuração, sanitização e opções do transporte.

Limites: este incremento não migra publicações, não consome a fila, não persiste IDs Telegram
e não oferece endpoint de envio. Fallback após rejeição de imagem depende do orquestrador com tentativas persistidas.
Não foram executados getMe ou publicação real. O script de secrets ainda deve ser executado pelo responsável com dados reais.
Checklist operacional: [TELEGRAM_CHECKLIST.md](integrations/TELEGRAM_CHECKLIST.md).

Verificações finais T1/T2:
- dotnet format Achadinhos.sln --verify-no-changes --no-restore: aprovado.
- Script telegram-secrets.ps1: sintaxe válida e smoke test aprovado com Read-Host/dotnet simulados,
  verificando envio do JSON por stdin e publicação desligada; nenhum secret real gravado.
- A execução direta inicial do smoke test encontrou a política local de scripts desabilitada.
  O teste passou em processo PowerShell separado com -ExecutionPolicy Bypass, sem mudar a política persistente.
## Telegram — fila, Worker e tentativa de homologação — 23/10/2026

Implementação:
- Publication evoluída sem duplicar tabela; canais e tentativas, snapshot jsonb, idempotência, lease, erro e IDs.
- Migration 20261023193729_TelegramDelivery e SQL infra/telegram-migration.sql.
- Backfill jsonb da migration revisado para {} antes de aplicar; registros antigos não ganham destino nem habilitação.
- Hangfire.NetCore 1.8.25 e Hangfire.PostgreSql 1.21.1 no Worker; servidor não é registrado na API.
- Restore inicialmente apontou NU1903 na dependência Newtonsoft.Json 11.0.1; corrigido com referência explícita 13.0.4.
- Claim e pacing transacionais, dispatcher recuperável, retry somente de rejeição segura, lease vencido para UnknownDelivery.
- Fallback após rejeição confirmada de imagem cria tentativa posterior persistida; não ocorre em resposta incerta.
- Endpoint de homologação JWT Admin com rate limit, disponível somente Development; bypass local recusado.
- CLI scripts/telegram.ps1: Check, Migrate, Test, Worker e Status. Test só habilita após Published confirmado.
- Processador limita a canal Development/IsTest; afiliados e publicação comercial continuam pendentes.

Validação real:
- Compilações dos projetos Tests e Worker em configuração Telegram: aprovadas, sem avisos/erros.
  A solução não define configuração Telegram (MSB4126 ao tentar build da solution com esse nome);
  os projetos aceitam essa configuração e foram usados para evitar DLLs bloqueadas pela API em execução.
- Suíte com TEST_POSTGRES carregado sem expor credenciais: 91 aprovados, zero falhas, zero ignorados.
- PostgreSQL: dois processadores concorrentes produzem um envio simulado; IDs e tentativas persistidos.
- Casos adicionais: 429/retry_after/limite, timeout sem retry, canal bloqueado, lease vencido,
  TTL/kill switch, fallback em duas tentativas, JWT Admin, 401 local/anônimo, 403 Reviewer/Viewer, 404 Production.
- Upgrade com fila antiga e downgrade/upgrade testados apenas em schema descartável.
- Migration aplicada no banco local de aplicação com sucesso; consulta confirma zero migrations pendentes.
- Hangfire inicializado/encerrado no PostgreSQL real com Telegram:Enabled=false: aprovado.
- Cliente real getMe: HTTP 403, resposta não JSON. Probes sem token no host e em rota com token fictício:
  HTTP 403, Content-Type text/html. Responsável reproduziu o diagnóstico no terminal.
- scripts/telegram.ps1 -Action Test -NoBuild: interrompeu no diagnóstico, sem enfileirar ou publicar.
- Estado final conferido: DeliveryEnabled=false, VerifiedChannels=0, TestPublications=0, UnknownDeliveries=0.

Acesso externo à Bot API permanece bloqueado; não houve publicação real ou validação das permissões do canal.
Não expusemos token, não alteramos proxy/TLS e não habilitamos envio apesar da falha.
A API/Worker de validação não substituíram os processos existentes; o runtime-check encerrou seu host ao terminar.
Operação e retomada: [TELEGRAM_CHECKLIST.md](integrations/TELEGRAM_CHECKLIST.md).

Verificações finais da entrega:
- dotnet restore Achadinhos.sln --locked-mode: aprovado.
- dotnet format Achadinhos.sln --verify-no-changes --no-restore: aprovado após aplicar formatação completa.
  O filtro inicial por pastas não alcançou todos os arquivos novos; a verificação detectou e a formatação completa corrigiu.
- dotnet ef migrations has-pending-model-changes (configuração Telegram): sem alterações pendentes no modelo.
- Sintaxe PowerShell de scripts/telegram.ps1: aprovada.
- Snapshot original preservado durante fallback; histórico ImageRejected seleciona texto na próxima tentativa.
  Suíte final após esse ajuste: 91 aprovados, zero falhas, zero ignorados.
## Simulação local no painel — 23/10/2026

- Tabela publication_simulations com FK e unique por PublicationId; migration LocalPublicationSimulation aplicada.
- API de simulação somente Development + Simulation:Enabled + policy Write; consulta com Read.
- Servidor local permite o fluxo sem login já existente; endpoints reais Telegram continuam exigindo JWT Admin.
- Simulação não usa Worker, HttpClient, token ou publisher; Publication.Status=Simulated, sem IDs/PublishedAt/tentativas reais.
- Idempotência, auditoria SimulationCompleted, retorno da oferta para Approved e prévia imutável.
- UI: botão Simular agora, estado Simulada, página /publicacoes/{id}/simulacao e aviso explícito de ausência de envio.
- Nenhuma oferta de demonstração inserida no banco da aplicação.

Validação:
- Build do backend em configuração isolada Telegram aprovado.
- 98 testes aprovados, zero falhas/ignorados, incluindo PostgreSQL real.
- Sete casos novos cobrem o fluxo HTTP local completo, cliques concorrentes, disputa com cancelamento,
  autorização, feature flag/Production, recusa de fila real e link inseguro.
- npm run lint, npm run typecheck e npm run build (NEXT_DIST_DIR=.local/simulation-build): aprovados.
- Nova API validada primeiro na porta 8081 e depois substituiu a API do projeto na porta 8080.
- Novo painel validado primeiro na porta 3001 e depois substituiu o Next.js do projeto na porta 3000.
  A primeira verificação encontrou a página antiga no servidor anterior; atualização do processo confirmou o novo texto.
- HTTP: Publicações/Ofertas/Revisão retornaram 200. API confirmou simulationEnabled=true, deliveryEnabled=false e fila vazia.
- Modelo EF sem alterações pendentes após migration. Nenhuma chamada à rede Telegram nesta tarefa.

Operação: [SIMULATION_WORKFLOW.md](SIMULATION_WORKFLOW.md).
