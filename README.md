# Achadinhos Engine

Fundação .NET 10 para descoberta autorizada de ofertas e publicação assistida no Telegram.
Este é o repositório do backend. O frontend Next.js possui repositório independente na pasta UI do workspace local. Coleta Shopee, score e publicação comercial permanecem no plano. Hangfire e envio de homologação Telegram estão implementados; conexão externa ainda bloqueada por HTTP 403. Cadastro manual, revisão e fila local estão implementados.

- [Plano executável e critérios de aceite](docs/IMPLEMENTATION_PLAN.md)
- [Especificação original](docs/TECHNICAL_PLAN_ORIGINAL.md)
- [Validação e limitações](docs/VALIDATION.md)
- [Decisão arquitetural](docs/decisions/0001-foundation.md)
- [Pendências do contrato Shopee](docs/integrations/SHOPEE_CHECKLIST.md)
- [Planejamento da integração Telegram](docs/integrations/TELEGRAM_INTEGRATION_PLAN.md)
- [Guia original da integração Telegram](docs/integrations/TELEGRAM_INTEGRATION_AGENT_GUIDE.md)

## Validar código

Requisitos: SDK indicado em global.json. Executar na raiz:

~~~powershell
dotnet tool restore
dotnet restore Achadinhos.sln --locked-mode
dotnet build Achadinhos.sln --no-restore
dotnet format Achadinhos.sln --verify-no-changes --no-restore
dotnet test Achadinhos.sln --no-build
~~~

O teste PostgreSQL será explicitamente ignorado sem TEST_POSTGRES.
Para executá-lo, defina TEST_POSTGRES com uma connection string de banco de teste e rode dotnet test.
O teste cria e remove somente um schema próprio, mas deve ser executado em banco descartável.
Os testes HTTP substituem apenas a consulta de ofertas e usam JWT assinados de teste; não provam consultas reais ao banco nem integração com um IdP externo.

## Executar com Docker

Requisitos: Docker Engine/Desktop com Compose e containers Linux, além de IdP OIDC.

~~~powershell
Copy-Item .env.example .env
# Edite .env: senha local, issuer HTTPS e audience do seu IdP.
docker compose --env-file .env -f infra/compose.yaml up --build -d
docker compose --env-file .env -f infra/compose.yaml logs api worker migrate
Invoke-RestMethod http://localhost:8080/health/live
Invoke-RestMethod http://localhost:8080/health/ready
~~~

Compose espera PostgreSQL saudável e migration concluída antes de iniciar os hosts.
API e banco publicados apenas em loopback; esta composição é para desenvolvimento.
Secrets em .env não são versionados nem incluídos no contexto Docker.
A infraestrutura de produção deverá fornecer TLS, secrets e configurações próprias.
Credenciais PostgreSQL no exemplo são placeholders, não são credenciais de produção.

## Executar os hosts com PostgreSQL local (ambiente atual)

PostgreSQL 18.6 disponível em localhost:5432. Bancos separados:
achadinhos para a aplicação e achadinhos_test para testes.
A migration inicial já foi aplicada e os testes relacionais passaram neste ambiente.

A conexão está em .env.local, ignorado pelo Git e pelo contexto Docker.
O script carrega as configurações apenas no processo atual e as restaura ao terminar.
Os hosts .NET não carregam esse arquivo automaticamente: use os comandos abaixo.

~~~powershell
# Aplicar novas migrations:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Migrate

# Iniciar a API em http://localhost:8080:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Api

# Em outro terminal, iniciar o Worker:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Worker

# Rodar todos os testes, incluindo PostgreSQL real:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Test
~~~

ExecutionPolicy Bypass vale somente para cada processo PowerShell acima, sem alterar a política do Windows.
Encerre os hosts com Ctrl+C. O worker registra heartbeat a cada 30 segundos e mantém descoberta desativada.

Em outra máquina, crie os bancos e um .env.local com estes campos, preenchendo suas credenciais:

~~~dotenv
ConnectionStrings__Engine=Host=localhost;Port=5432;Database=achadinhos;Username=SEU_USUARIO;Password=SUA_SENHA
TEST_POSTGRES=Host=localhost;Port=5432;Database=achadinhos_test;Username=SEU_USUARIO;Password=SUA_SENHA
~~~

Opcionalmente configure Authentication__Authority e Authentication__Audience nesse arquivo para usar o IdP.
O script Api habilita acesso local sem login em Development. Consulte docs/LOCAL_WORKFLOW.md. Fora desse modo, endpoints administrativos continuam exigindo token válido.
Readiness retorna 503 se o banco estiver indisponível ou houver migration pendente; liveness continua 200.
O teste relacional cria/remove apenas seu schema isolado dentro de achadinhos_test.

## Autorização e rotas

Use access token do IdP configurado, audience correta e claim role com Admin, Reviewer ou Viewer.
A API valida assinatura, emissor, audience e expiração via JWT Bearer/OIDC.
A criação de usuários, cliente OIDC e login do painel são configurações/entregas posteriores.
Fora do modo local explícito, endpoints administrativos exigem IdP/token válido.

| Rota | Acesso |
| --- | --- |
| GET /health/live | Público |
| GET /health/ready | Público, sem detalhes do banco |
| GET /api/offers?page=1&pageSize=25 | Admin, Reviewer, Viewer |
| GET /api/integrations | Admin |
| GET /openapi/v1.json | Publico; apenas Development |

Lista de ofertas retorna items/page/pageSize/total; paginação limitada a 100 registros.
Nenhuma oferta artificial é carregada. O painel permite cadastrar ofertas manualmente.
Configuração de CORS não está aberta; integração com frontend será definida na fase do painel.
Todos os comandos deste README são executados dentro da pasta API, raiz deste repositório. A configuração PostgreSQL foi preservada em .env.local.

## Swagger local

Com a API iniciada, abra http://localhost:8080/swagger.
A raiz http://localhost:8080/ redireciona para essa interface em Development.
O Swagger e seu documento OpenAPI ficam disponiveis apenas em Development.
Para testar rotas administrativas, clique em Authorize e informe um access token JWT
valido do IdP configurado. As regras de acesso da API continuam sendo aplicadas.
## Fluxo local do painel

Login adiado. Cadastro, consulta, revisão e fila de agendamentos: [operação local e novos endpoints](docs/LOCAL_WORKFLOW.md). Aplique a migration LocalWorkspace antes de iniciar a API atualizada. O script Api habilita o acesso sem login somente em Development e loopback. Agendamentos comuns ainda não são publicados; o teste Telegram usa um caminho explícito de homologação.

## Configurar secrets Telegram

O Worker executa a fila explícita de homologação com Hangfire e PostgreSQL.
Publicação desligada por padrão. Bot e canal já foram criados pelo responsável; conectividade ainda pendente.
Para salvar token e destino localmente, sem expor o token nos argumentos ou histórico:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram-secrets.ps1
~~~

Execute em seu terminal e informe os dados somente na entrada interativa.
O script usa User Secrets do Worker e mantém o envio desligado; não publica nem testa conectividade.
Próximos passos e evidências: [checklist Telegram](docs/integrations/TELEGRAM_CHECKLIST.md).
## Telegram — fila e teste real

Worker/Hangfire e persistência de tentativas/IDs implementados. Migration TelegramDelivery aplicada localmente.
91 testes aprovados, incluindo PostgreSQL real. A rede retorna HTTP 403 (HTML) em api.telegram.org,
inclusive sem token; nenhuma mensagem foi publicada e o envio está desligado.

Após liberar acesso HTTPS à Bot API, execute na raiz API:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram.ps1 -Action Test
~~~

O comando verifica bot/canal privado/permissões, enfileira um teste fictício idempotente, executa pelo Worker
e só habilita envio após Published confirmado. Para consultar sem expor secrets, use -Action Status.
A fila comum e seus agendamentos antigos não são liberados para publicação comercial.
[Operação, comandos e pendências](docs/integrations/TELEGRAM_CHECKLIST.md).
## Publicação simulada sem Telegram

O painel permite validar cadastro, aprovação, agendamento e prévia sem conexão externa.
Na fila, use **Simular agora**. O registro fica **Simulada · sem envio** e a oferta volta para aprovada.
Não há mensagem, ID Telegram ou tentativa de entrega fictícia. Agendamentos reais não são convertidos em simulação.
O script dev.ps1 -Target Api habilita Simulation:Enabled=true apenas em Development.
[Fluxo, endpoints e validação](docs/SIMULATION_WORKFLOW.md).