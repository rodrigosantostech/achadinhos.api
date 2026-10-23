# ADR 0002 — API e UI em repositórios separados

Status: aceito. Data: 23/10/2026. Decisão explícita do usuário.

Substitui a organização em monorepo prevista no TDD original.
O workspace local possui API/ e UI/, cada qual com .git, CI, README e dependências independentes.
A raiz local contém apenas orientações e configuração de editor; não é repositório pai.

API mantém solution, backend, testes, infraestrutura, scripts, docs e segredos locais já existentes.
UI contém Next.js/TypeScript/App Router e se comunica com API por HTTP.
Não há referências a arquivos compartilhados entre os dois repositórios nem acesso da UI ao banco.
API_BASE_URL é uma variável do servidor Next.js. O health check é público e a verificação inicial
ocorre no servidor, sem alterar autenticação ou abrir CORS da API.

A base do frontend foi antecipada por solicitação do usuário; os fluxos funcionais e login
continuam na fase administrativa do plano. Nenhum repositório remoto ou publicação foi criado.