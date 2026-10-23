# Gate Shopee Brasil

Estado: contrato e acesso não validados. Nenhuma chamada à conta foi feita.

Antes de implementar o adapter, registrar:

- Conta aprovada e app com acesso ao programa Affiliate Brasil.
- Documentação oficial da conta: https://open-api.affiliate.shopee.com.br/explorer
- Operações efetivamente liberadas, schemas e versão/região.
- Assinatura/autenticação oficial, validade temporal e tolerância de relógio.
- Quotas e política de retry; paginação, ordenação e consistência de cursor.
- Campos de preço, moeda, estoque, vendedor, imagem e condições/cupom.
- Operação permitida de geração de link, tracking/sub-ID e expiração.
- Fixtures sanitizadas com data, operação e origem; remover tokens e informações pessoais.
- Limites de armazenamento/retenção de raw payload e uso de imagens.
- Teste autenticado mínimo autorizado e resultado sanitizado.

Guardar credenciais em variáveis protegidas/secret manager, nunca neste arquivo ou em fixtures.
Não enviar credenciais em chat nem assumir endpoints de outras APIs da Shopee.