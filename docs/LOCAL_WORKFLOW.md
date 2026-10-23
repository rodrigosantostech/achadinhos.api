# Operação local sem login

Atualizado em 23/10/2026. Login adiado por decisão do usuário.

## Iniciar

Na pasta API:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Migrate
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/dev.ps1 -Target Api
~~~

Em outro terminal, na pasta UI:

~~~powershell
npm.cmd run dev
~~~

Abra http://localhost:3000. API em http://localhost:8080.

O script de desenvolvimento passa LocalDevelopment:Enabled=true à API. O acesso sem token exige
ambiente Development, IP remoto de loopback, Host localhost/loopback e ausência de Origin e Authorization.
O Next consulta a API no servidor. Não é necessário abrir CORS.
Production e a execução sem essa opção continuam exigindo JWT. Token enviado não recebe bypass.

## Fluxo entregue

1. Abra Ofertas e cadastre título, URL HTTP/HTTPS e preço em BRL; preço original é opcional.
2. A oferta entra como A revisar, com uma observação de preço manual.
3. Em Revisão, abra o detalhe e aprove ou rejeite. Rejeição exige motivo.
4. Uma oferta aprovada pode ser agendada para data futura até 365 dias, informada em UTC−3.
5. Publicações mostra a fila persistida. Cancelar devolve a oferta ao estado Aprovada.
6. O detalhe exibe até 100 preços e 100 decisões mais recentes.

Busca por título, filtro por estado e paginação estão disponíveis. Os dados ficam no PostgreSQL.
O cadastro manual não coleta nem verifica informações da loja. O preço original informado não comprova desconto histórico.

## Contratos HTTP

Rotas existentes de health e integrações foram mantidas.

| Método | Rota | Operação |
| --- | --- | --- |
| GET | /api/offers?page=1&pageSize=20&status=Discovered&search=produto | Lista filtrada; status e search opcionais |
| POST | /api/offers | Cadastro manual: title, productUrl, price, originalPrice |
| GET | /api/offers/{id} | Detalhe, preços e revisões |
| POST | /api/offers/{id}/review | decision Approved/Rejected e reason |
| POST | /api/offers/{id}/schedule | scheduledAt como ISO 8601 com fuso |
| GET | /api/publications?page=1&pageSize=20 | Agendamentos; deliveryEnabled=false |
| POST | /api/publications/{id}/cancel | Cancela agendamento ativo |

Read: Admin, Reviewer, Viewer. Write: Admin, Reviewer.
Cadastros retornam 201; revisão/cancelamento 204; dados inválidos 400; recurso ausente 404; estado incompatível 409.
Somente oferta aprovada pode ser agendada. Há índice único parcial para um agendamento ativo por oferta.
Transições usam updates condicionais e transações; cancelar duas vezes retorna conflito.

## Próximas integrações

A fila não envia mensagens e não muda para publicada após o horário. O Worker ainda só executa heartbeat.
Coleta Shopee, afiliados, avaliação automática, bot/canal Telegram e executor da fila seguem pendentes.
O envio real exige configurar a integração e suas regras de entrega. Nenhum dado de oferta é inserido pela migration;
ela cria apenas as tabelas de revisão/fila e a origem Cadastro manual.
