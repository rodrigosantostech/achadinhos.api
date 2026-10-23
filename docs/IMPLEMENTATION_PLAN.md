# Plano de implementação — Achadinhos Engine

Data: 23/10/2026. Base: TECHNICAL_PLAN_ORIGINAL.md, preservado sem alterações.
Repositório: C:/Dev/projeto-teste/API.

## Estratégia e capacidade

API e UI ficam em repositórios independentes (ADR 0002). A base Next.js foi antecipada por solicitação do usuário; os fluxos completos do painel continuam na Fase 5. Uma pessoa desenvolvedora, trabalho sequencial por dependências.
Estimativas em dias úteis de engenharia, sem contar espera por acesso externo, homologação e infraestrutura.
Total inicial: 22–33 dias; revisar após a primeira coleta real. Quatro semanas do TDD são uma hipótese, não compromisso.

O Sprint 1 do TDD foi dividido em Fase 1 (fundação executável) e Fase 2 (contrato autorizado + primeira coleta).
Sem credenciais aprovadas não há como aceitar a entrega de coleta real. Dados de teste jamais contam como evidência comercial.
IA não participa do caminho crítico e não exige credenciais nesta implementação.

## Fase 1 — Fundação (2–3 dias; código implementado nesta execução)

Objetivo: compilar e testar backend separado em API, worker, domínio, aplicação, contratos e infraestrutura.

| ID | Entrega | Dependência | Evidência / aceite | Estado |
| --- | --- | --- | --- | --- |
| F1.1 | Solution .sln, .NET 10, referências direcionais, Git e convenções | SDK | Build sem avisos/erros | Implementado; verificar em VALIDATION.md |
| F1.2 | Marketplace, Offer, PriceSnapshot, IntegrationRun e migration | F1.1 | numeric(18,2), jsonb, UTC, FK e unique por oferta externa | Migration aplicada em PostgreSQL 18.6 local; teste relacional aprovado |
| F1.3 | API: health, ofertas paginadas, status de integração, ProblemDetails e correlação | F1.2 | Liveness independente; readiness exige banco e schema atualizado | Implementado |
| F1.4 | JWT/OIDC e roles Admin, Reviewer, Viewer | F1.3 | 401 anônimo, 403 role insuficiente, token inválido rejeitado | API implementada/testada; IdP externo a configurar |
| F1.5 | Worker separado com heartbeat, coleta desligada | F1.2 | Host independente, encerramento por cancelamento | Implementado; heartbeat com DatabaseReady=True validado localmente |
| F1.6 | Compose PostgreSQL/API/Worker e migration como tarefa exclusiva | F1.2 | Subida de banco, migration e hosts, sem corrida de migrações | Arquivos criados; Docker ausente no ambiente |
| F1.7 | Lockfiles e CI build, format, testes e teste real PostgreSQL | F1.1–F1.6 | Pipeline verde em GitHub Actions | Workflow criado; execução remota pendente |

Limites: base Next.js em repositório separado criada por solicitação posterior; sem login/CRUD no painel, Hangfire, provider Shopee funcional, jobs de descoberta, ofertas artificiais ou publicação Telegram.
A autenticação do painel depende da Fase 5; esta fase entrega proteção da API, não login de usuário no navegador.
O worker verifica conectividade/schema: ainda não é executor de jobs.

Ambiente atual: PostgreSQL local, sem dependência de Docker. Migration, API e worker validados em runtime e teste PostgreSQL aprovado. Homologação do IdP permanece pendente; validação dos artefatos Docker pode ocorrer posteriormente.
Não marcar validação Docker/banco como concluída apenas porque build e testes HTTP passaram.

## Fase 2 — Contrato Shopee e primeira coleta (4–7 dias após acesso)

Dependências: F1; conta Shopee Brasil aprovada; app autorizado; documentação e quotas efetivas da conta.

1. F2.1 Registrar operações liberadas, região, autenticação/assinatura, paginação, limites e campos retornados em docs/integrations.
2. F2.2 Obter fixtures sanitizadas autorizadas; implementar ShopeeProvider por IMarketplaceProvider; testes de assinatura, paginação, erro e payload incompleto.
3. F2.3 Configurar Hangfire com storage PostgreSQL compatível e versões verificadas; servidor apenas no Worker. API apenas agenda jobs. Não expor dashboard público.
4. F2.4 Endpoint Admin POST /api/integrations/shopee/sync retorna 202 + run ID; idempotency key e auditoria; registrar run antes de enfileirar, com outbox ou recuperação de runs pendentes.
5. F2.5 Upsert transacional usando chave marketplace/external_offer_id; preservar campos conhecidos em respostas parciais; snapshot por observação identificável; não deduplicar apenas por valor do preço.
6. F2.6 Quotas, cancelamento, timeout, backoff com jitter e classificação de erro; cursor persistido e avanço somente após commit.
7. F2.7 Validar coleta real via API -> job -> provider -> PostgreSQL -> GET /api/offers, com métricas e evidência sanitizada.

Aceite: coleta de pelo menos duas páginas autorizadas quando disponíveis, reprocessamento sem duplicatas, concorrência testada em PostgreSQL e run consultável.
Se acesso permanecer pendente: testar contratos internos com fixtures explicitamente de teste, sem afirmar integração oficial homologada.

## Fase 3 — Pricing e avaliação explicável (4–5 dias)

Depende de F2 para homologação real; regras podem ser construídas com fixtures enquanto o acesso é liberado.

1. F3.1 Criar avaliações versionadas, reasons, confidence e consulta histórica por oferta.
2. F3.2 Implementar gates de URL, preço, origem habilitada, frescor e bloqueio de vendedor.
3. F3.3 Definir score v1: componentes ausentes permanecem ausentes; registrar cobertura das evidências e política conservadora. Não renormalizar pesos sem ADR.
4. F3.4 Expor detalhe/histórico e reavaliação por job; avaliar thresholds 70/85 com amostra real revisada.
5. F3.5 Testar ausência de histórico, preço cheio anunciado, preço condicionado a cupom, BRL, dados obsoletos e execução repetida.

Aceite: cada decisão possui versão, origem, confiança e justificativas reproduzíveis; nenhuma afirmação histórica sem evidência.
Revisão humana obrigatória até calibrar precisão com amostra real.

## Fase 4 — Publicação assistida e afiliados (5–7 dias)

Depende de F2/F3, contrato de link afiliado autorizado e bot administrador em canal Telegram de teste.

1. F4.1 Criar canais, links, fila, publicações e auditoria; unique de idempotência e índices de deduplicação com campanha nula/não nula.
2. F4.2 Máquina de estados explícita; comandos aprovar, rejeitar, agendar e cancelar com RBAC e auditoria.
3. F4.3 Reserva por lease atômico; tratar worker morto. Sending com lease expirado vira entrega incerta, sem reenvio automático.
4. F4.4 Preflight de preço/estoque/frescor e geração/validade de afiliado; cancelamento quando dados inválidos.
5. F4.5 Adapter Telegram: sendPhoto/sendMessage, escape de texto, divulgação de afiliado e persistência de message_id.
6. F4.6 Tratar 429/retry_after e erros definitivos. Timeout após envio => UnknownDelivery; revisão/reconciliação manual.
7. F4.7 Kill switches independentes para coleta e publicação; templates apenas com fatos conhecidos.

Aceite: fluxo assistido completo no canal de teste, duas instâncias sem envio concorrente, teste de timeout após envio e nenhuma repetição cega.
Fallback de foto para texto apenas quando houver certeza de que a primeira chamada não publicou.

## Fase 5 — Admin e operação (4–6 dias)

Depende dos contratos HTTP de F3/F4 e de IdP com client configurado para navegador.

1. F5.1 Evoluir a base Next.js/TypeScript do repositório UI: login OIDC com sessão segura e autorização coerente; nenhuma credencial de bot/provider no browser.
2. F5.2 Dashboard, ofertas/detalhe, revisão e publicações com paginação e polling limitado.
3. F5.3 Integrações e configurações com auditoria; entregas incertas visíveis e revisão explícita.
4. F5.4 Testes de login, acesso negado e aprovação/agendamento; CORS restrito caso front e API tenham origens diferentes.

Aceite: operador revisa e agenda sem ferramentas de desenvolvedor; Viewer não altera dados; nenhuma URL sensível em logs/telas.

## Fase 6 — Homologação e produção (3–5 dias)

Depende das fases anteriores, hospedagem, domínio, segredos, política editorial e backup definidos.

1. F6.1 OpenTelemetry, métricas e alertas de fila, 429, credenciais, falhas e entrega incerta; sanitização e retenção.
2. F6.2 Testes E2E de coleta/revisão/publicação e falhas com PostgreSQL real; ensaio de concorrência e restart.
3. F6.3 Backups e restore em banco separado; deploy containerizado com migration controlada e procedimento de rollback.
4. F6.4 Homologar termos/disclosure dos programas e minimização/retenção dos dados. Redirect de cliques somente se houver decisão explícita.
5. F6.5 Operação assistida primeiro; automação por canal apenas após precisão/frescor e alertas homologados.

Aceite: checklist dos dez itens do MVP no TDD com evidência registrada, restore demonstrado e kill switch exercitado.

## Riscos e decisões necessárias

| Dependência | Quando resolver | Efeito |
| --- | --- | --- |
| Shopee: acesso, contrato, quotas e fixtures | Antes de F2.2 | Bloqueia integração real; não substituível por scraping presumido |
| PostgreSQL local | F1 validado | Bancos criados, migration aplicada e teste real aprovado; Docker fica opcional para desenvolvimento |
| IdP: issuer, audience, role claim e usuários | F1 operacional / F5 | API está protegida; acesso administrativo exige token válido |
| Categorias e política editorial | Antes da calibração F3 | Define demanda, frequência e revisão |
| Bot e canal de teste | Antes de F4.5 | Bloqueia aceite do envio real |
| Hospedagem, secret manager e backups | Antes de F6 | Bloqueia produção |

Próximo trabalho técnico: configurar/homologar o IdP e preencher docs/integrations/SHOPEE_CHECKLIST.md antes de codificar o adapter.
## Atualização de escopo local — 23/10/2026

Por decisão do usuário, login/IdP foi adiado. Antecipados cadastro manual de ofertas, detalhe/histórico,
revisão com justificativa, agendamento e cancelamento persistidos, além das telas correspondentes.
Ver LOCAL_WORKFLOW.md. Esta entrega não conclui F2, F3 ou F4: coleta Shopee, score, afiliados e
envio Telegram ainda não foram implementados. A fila local não é executor de publicação.
A prioridade deixa de ser configurar IdP; os próximos passos são contrato/coleta Shopee e integração
de entrega, conforme as credenciais efetivamente disponíveis.

## Planejamento detalhado Telegram — 23/10/2026

O [plano de integração Telegram](integrations/TELEGRAM_INTEGRATION_PLAN.md) detalha a Fase 4
com base na fila manual existente, sem recriar Publication nem a solução. O
[guia original](integrations/TELEGRAM_INTEGRATION_AGENT_GUIDE.md) foi preservado sem alterações.
Contratos e testes do adapter podem ser antecipados com fixtures; homologação comercial
continua dependendo de link afiliado autorizado, dados válidos e canal de teste.
Este acréscimo é documental: envio Telegram e Hangfire permanecem pendentes.
## Telegram — evolução do Worker e fila — 23/10/2026

Publication, tentativas/canais, migration e Hangfire implementados para homologação privada.
API apenas enfileira; envio permanece no Worker. Testes: 91 aprovados com PostgreSQL real.
Teste de conectividade retornou HTTP 403 também em probes sem token; publicação ficou desligada.
A primeira mensagem real e habilitação dependem de acesso HTTPS a api.telegram.org.
Publicação comercial, geração de afiliados e produção não foram liberadas.
Ver integrations/TELEGRAM_CHECKLIST.md.