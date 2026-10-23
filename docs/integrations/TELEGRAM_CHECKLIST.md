# Telegram — operação e homologação

Atualização: 23/10/2026. [Planejamento](TELEGRAM_INTEGRATION_PLAN.md).

## Resultado atual

Implementados e validados localmente:
- Cliente Telegram, payloads texto/foto, escaping, botão HTTPS e tratamento sanitizado de falhas.
- Migration TelegramDelivery aplicada no banco local; tabelas de canais/tentativas e campos de snapshot, lease e IDs.
- Hangfire com PostgreSQL no Worker, dispatcher recuperável e envio somente por job.
- Claim atômico, exclusão de concorrência, pacing por canal, limite de três tentativas para rejeições repetíveis.
- 429 respeita retry_after; timeout e entrega incerta não são reenviados automaticamente.
- Foto rejeitada de forma confirmada gera fallback textual em outra tentativa persistida.
- Endpoint de teste com JWT Admin, limitação de chamadas e disponível apenas em Development.
- A identidade local sem login não autoriza esse endpoint.
- CLI local explícita permite homologação mesmo com login do painel adiado.

**Bloqueio externo:** getMe retornou HTTP 403 com resposta não JSON. O responsável confirmou o mesmo resultado em seu terminal.
Probes públicos, sem credencial, também receberam HTTP 403 e text/html em api.telegram.org.
Isso indica bloqueio de acesso ao host; não comprova token inválido. Não foi alterada configuração de proxy/TLS para contornar o bloqueio.

Estado conferido no banco:
- DeliveryEnabled=false.
- PendingMigrations=0.
- VerifiedChannels=0.
- TestPublications=0.
- UnknownDeliveries=0.

Nenhuma mensagem foi publicada, nenhum ID Telegram real foi persistido e o envio permaneceu desligado.
91 testes passaram com PostgreSQL real e respostas Telegram simuladas. Inicialização e encerramento do Hangfire passaram com envio desligado.

## Retomar após liberação de rede

Permitir acesso HTTPS a api.telegram.org na rede/proxy/firewall aplicável. Não envie o token ao agente nem em solicitações de suporte.
Na raiz API:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram.ps1 -Action Check
~~~

Após o diagnóstico funcionar, executar o fluxo completo autorizado de teste/habilitação:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram.ps1 -Action Test
~~~

A rotina:
1. Desliga o envio antes da validação.
2. Verifica bot, canal e permissão de postar.
3. Exige canal privado, registra destino e prepara oferta explicitamente fictícia, com preço R$ 1,00 e link controlado example.com.
4. Cria uma publicação idempotente, revisão e snapshot; o comando de preparação não envia.
5. Inicia Hangfire/Worker temporariamente e processa somente a chave de teste selecionada.
6. Aguarda até 90 segundos e exibe estado/IDs persistidos, sem token.
7. Somente após Published confirmado grava Telegram:Enabled=true nos User Secrets do Worker.

O teste deixa dados identificados como fictícios no banco, para auditoria e consulta no painel. Não representa oferta comercial ou link afiliado homologado.
A mesma TestKey impede postagem duplicada ao repetir o comando. Não troque a chave para contornar UnknownDelivery.
Se uma chave expirar ou terminar Failed, revise a causa e autorize conscientemente novo teste com -TestKey.

Para manter o executor ativo após o teste:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram.ps1 -Action Worker
~~~

Consultar estado:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/telegram.ps1 -Action Status -NoBuild
~~~

Desligar:

~~~powershell
dotnet user-secrets set "Telegram:Enabled" "false" --project backend/Achadinhos.Worker
~~~

Reinicie o Worker ao rotacionar token/destino. O kill switch é relido pelo dispatcher/processador; chamadas HTTP já iniciadas precisam ser conciliadas.
User Secrets é armazenamento local de desenvolvimento, sem criptografia. Não listar secrets para compartilhar diagnóstico.

## API e fila

- POST /api/admin/telegram/test: body com idempotencyKey de 8 a 100 caracteres; retorna 202 e publicationId.
- GET /api/admin/telegram/publications/{id}: consulta estado, IDs e erro estruturado.
- Rotas exigem JWT Admin; não aceitam a identidade LocalDevelopment. Limite: cinco solicitações de teste por minuto.
- Rotas não são mapeadas em Production.
- Destino não pode ser escolhido no request. Preparação/validação do canal pertence ao Worker.
- GET /api/publications inclui IsTest, IDs, estado e metadados de tentativas.
- Agendamentos antigos continuam sem canal/snapshot de entrega e não são enviados automaticamente.
- deliveryEnabled=false na fila comum é intencional: esta entrega libera apenas o caminho explícito de homologação, não ofertas comerciais.
- A API já em execução foi preservada; reinicie-a para carregar os endpoints novos. A migration é compatível com os comandos locais antigos.

## Falhas e conciliação

Publication pode estar Scheduled, Processing, Published, RetryScheduled, Failed, UnknownDelivery ou Cancelled.
O dispatcher recupera registros elegíveis se houver falha entre persistência e criação do job.

Processing com lease vencido vira UnknownDelivery e bloqueia o canal. Não há conversão automática de volta para Scheduled.
Se Telegram confirmar e o banco falhar, o lease vencido exige a mesma conciliação; evidência confirmada também é registrada na tentativa quando possível.

Antes de desbloquear, o operador deve procurar a mensagem no canal e registrar resultado/IDs reais e motivo.
Uma interface/endpoint dedicado de conciliação ainda está pendente. Não alterar estados para forçar reenvio durante investigação.
Erros/tentativas não guardam token, URI autenticada ou descrição bruta da API.

## Gates restantes

- [x] Estrutura de persistência e migration local.
- [x] Concorrência, tentativas, 429, TTL, kill switch, timeout e fallback testados.
- [x] JWT Admin, recusa do acesso local e restrição de ambiente testados.
- [x] Bot/token preparados pelo responsável; armazenamento fora do código.
- [ ] Rede permitindo acesso à Bot API.
- [ ] getMe/getChat/getChatMember reais aprovados.
- [ ] Primeira mensagem real pelo Worker e IDs persistidos.
- [ ] Habilitação após teste real confirmado.
- [ ] Comandos de conciliação e métricas/alertas operacionais completos.
- [ ] Publicação comercial com origem/estoque/preço/link afiliado autorizados.
- [ ] Produção: canal separado, secrets, identidade, observabilidade e aprovação humana.

O processador atual limita publicação a IsTest e canal Development verificado nas últimas 24 horas.
Produção com Telegram:Enabled=true é recusada no startup. Não há publicação comercial automática nesta etapa.

## Referências

- [Bot API](https://core.telegram.org/bots/api).
- [Hangfire: retry por job](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html).
- [Storage PostgreSQL](https://github.com/hangfire-postgres/Hangfire.PostgreSql).

## Continuidade sem VPN/Telegram

Implementado modo de simulação local no painel, independente da conexão Bot API e de secrets.
Ver [SIMULATION_WORKFLOW.md](../SIMULATION_WORKFLOW.md). Status Simulated e registros próprios preservam
a distinção entre prévia local e entrega real; os gates de homologação real acima continuam pendentes.