# ADR 0001 — Fundação antes dos conectores

Status: aceito para implementação inicial, 23/10/2026.

Manter monólito modular com dois executáveis e quatro bibliotecas. Dependências:
API/Worker -> Infrastructure -> Application -> Domain/Contracts.
Contracts e Domain não referenciam infraestrutura. Application define portas; Infrastructure implementa persistência.

Os módulos Discovery e Pricing começam com entidades núcleo agrupadas no domínio e contexto EF compartilhado.
Não há assembly por módulo enquanto isso não trouxer benefício. A API usa IOfferQueries para leitura; nenhuma escrita arbitrária exposta.
Separar configurações e casos de uso por módulo conforme forem implementados.

DiscoveryPage inclui NextCursor: a lista simples do TDD perderia o estado da paginação. Isso é contrato interno, não afirmação de schema Shopee.

Migration é executada por tarefa exclusiva antes dos hosts. API e worker não chamam Migrate automaticamente.
PostgreSQL usa jsonb, numeric(18,2), timestamps com fuso e restrições relacionais.
Nomes de tabelas snake_case; colunas seguem propriedades C# com identificadores citados no SQL gerado.

JWT recebido de IdP OIDC externo; validação por metadados HTTPS e role claim "role".
Não há emissor de tokens, usuários/senhas ou login implementado no frontend nesta fase.
Roles Read: Admin/Reviewer/Viewer; status de integrações: Admin.
OpenAPI disponível em Development e protegido; endpoints health públicos sem detalhe de infraestrutura.

Worker apenas heartbeat e detecção de schema pendente; Hangfire chega com a primeira coleta.
Nenhum ShopeeProvider fictício registrado em DI: falta contrato autorizado.
Não criar Next.js antes de existir o fluxo assistido a operar.
Referências verificadas:
- EF Core PostgreSQL 10: https://www.npgsql.org/efcore/release-notes/10.0.html
- JWT Bearer ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0