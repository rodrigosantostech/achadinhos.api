# Simulação local de publicação

Data: 23/10/2026. Funciona sem Telegram, bot, token, Worker ou acesso externo.

## Usar no painel

1. Abra http://localhost:3000/ofertas e cadastre uma oferta com link HTTPS.
2. No detalhe, aprove a oferta e salve um agendamento futuro.
3. Abra Publicações e clique em **Simular agora**.
4. Confira a prévia e o registro marcado **Simulada · sem envio**.
5. A oferta volta para Aprovada e pode receber outro agendamento. O registro anterior permanece consultável.

A simulação é manual e pode ser executada antes do horário escolhido. Ela encerra apenas aquele agendamento local.
Não existe publicação automática simulada no horário; o botão é a ação explícita do operador.
Não são criadas ofertas de demonstração na inicialização nem dados artificiais no banco da aplicação.

## Separação de entrega real

- Resultado local em publication_simulations, com unicidade por PublicationId, operador, horário, snapshot e texto.
- Publication recebe Status=Simulated; TelegramChatId, TelegramMessageId e PublishedAt permanecem nulos.
- Nenhuma tentativa de entrega, canal ou mensagem do Telegram é criada pela simulação.
- O simulador não depende de HttpClient, ITelegramPublisher, secrets ou Hangfire.
- Agendamentos com canal Telegram ou IsTest=true são recusados; resultados simulados não são elegíveis para o job real.
- O token e o kill switch do Telegram não são lidos ou alterados pelo simulador.
- Cliques repetidos devolvem o mesmo registro; concorrência com cancelamento possui apenas um vencedor.
- Oferta retorna a Approved e ganha evento de auditoria SimulationCompleted.
- Snapshot/preview são preservados mesmo que a oferta mude posteriormente.

O formatter é reutilizado, mas o aviso publicitário é substituído por informação explícita de simulação e afiliação não validada.
OriginalPrice manual não aparece como desconto ou preço de referência.
O texto é renderizado como conteúdo React, sem dangerouslySetInnerHTML. O link somente abre após clique do operador.

## Configuração e endpoints

Disponível somente em Development. A ação exige Simulation:Enabled=true e policy Write (Admin/Reviewer ou modo local autorizado).
O script scripts/dev.ps1 -Target Api já inclui a flag; autenticação/loopback do modo local continuam sendo aplicados.
Produção não mapeia os endpoints; o modo real de Telegram continua separado e desligado.

- POST /api/publications/{id}/simulate: conclui simulação de agendamento local pendente e retorna recibo com telegramSent=false.
- GET /api/publications/{id}/simulation: consulta prévia e auditoria; policy Read.
- GET /api/publications: simulationEnabled e canSimulate controlam disponibilidade da ação no painel.

Migration: 20261023200537_LocalPublicationSimulation. Aplicada no banco local.
Ao preparar outro ambiente, aplicar migrations antes de iniciar a API.

## Ambiente atual e validação

API atualizada em localhost:8080, com SimulationEnabled=true e deliveryEnabled=false na fila comum.
Painel atualizado em localhost:3000, usando o build isolado UI/.local/simulation-build.
PID dos processos iniciados registrado em API/.local/simulation-api.pid e UI/.local/simulation-ui.pid.
A pasta de build isolada é selecionada por NEXT_DIST_DIR; o padrão do projeto permanece .next.

98 testes aprovados com PostgreSQL real, sem ignorados. Casos novos:
- fluxo HTTP local de cadastro, revisão, agendamento, simulação e reagendamento;
- duas simulações concorrentes produzem um único recibo;
- cancelamento concorrente possui um único vencedor;
- recusa de fila Telegram e URL insegura;
- autorização, flag desligada e ausência de rotas em Production;
- auditoria e snapshot preservados, sem IDs ou tentativas Telegram.

UI: lint, typecheck e build de produção aprovados. Páginas Publicações, Ofertas e Revisão verificadas por HTTP.
O painel da aplicação permanece sem ofertas de teste; a validação funcional usou schemas descartáveis em banco de testes.
A confirmação de entrega real continua pendente de rede e não é comprovada por esta funcionalidade.
