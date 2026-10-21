# API REST

Documento OpenAPI gerado em `/openapi/v1.json`, acessível somente por administrador autenticado. Importe o JSON em um cliente OpenAPI para navegação. Não há dependência de CDN para documentação.

## Sessão

`POST /api/auth/login`, JSON `{"login":"admin","password":"sua senha configurada"}` e cabeçalho `X-MeuBar: 1`. A resposta define cookie HttpOnly de sessão. Guarde o cookie em um cookie jar de seu cliente; não tente ler o token com JavaScript. `GET /api/auth/me` informa perfil e autorizações; `POST /api/auth/logout` revoga a sessão.

Todos os endpoints abaixo exigem sessão, exceto health e rotas de serviço explicitamente identificadas. Toda mutação de navegador exige `Content-Type: application/json` e `X-MeuBar: 1`. Não envie senhas em query strings.

## Comandos idempotentes

Inclua `operationId` (novo UUID por operação lógica). Reenvie o mesmo UUID e o mesmo conteúdo se a confirmação falhar. Não gere outro UUID cegamente após timeout. Identificador repetido com conteúdo diferente retorna 409. Cadastros editáveis usam `version` recebido na leitura anterior.

| Rota | Operação / autorização |
|---|---|
| `GET /health` | Prontidão do banco; sem autenticação |
| `GET /api/products` | Catálogo; custo oculto ao funcionário |
| `POST /api/products`, `PUT /api/products/{id}` | Cadastro/preço; administrador |
| `POST /api/products/{id}/stock` | Quantidade assinada e motivo; administrador |
| `GET /api/stock` | Movimentos; administrador |
| `GET /api/customers` | Cadastro completo para admin; nomes de autorizados para atendimento |
| `POST /api/customers`, `PUT /api/customers/{id}` | Cadastro/crédito; administrador |
| `GET /api/customers/{id}/statement` | Vendas, contas, pagamentos, saldo; administrador |
| `GET /api/tables`, `GET /api/tabs` | Mesas e comandas ativas |
| `POST /api/tables` | Cadastro de mesa; administrador |
| `POST /api/tabs` | `tableId` opcional, `consumer`, `notes` |
| `GET /api/tabs/{id}` | Itens, total e histórico da comanda |
| `POST /api/tabs/{id}/items` | `productId`, `quantity`; preço vem do servidor |
| `POST /api/tabs/{id}/request-close` | Solicita fechamento |
| `POST /api/tabs/{id}/close` | Fechamento financeiro; recebimentos exigem autorização |
| `POST /api/items/{id}/cancel`, `/api/tabs/{id}/cancel` | Cancelamento com `reason`; administrador |
| `POST /api/tabs/{id}/transfer` | `tableId` destino livre; administrador |
| `POST /api/tabs/{id}/merge` | `sourceTabId` origem aberta; administrador |
| `GET /api/receivables`, `GET /api/payments` | Administrador ou recebedor autorizado |
| `POST /api/receivables/{id}/receive` | `amount`, `method`; recebedor autorizado |
| `POST /api/payments/{id}/reverse` | Estorno de fiado com motivo; administrador |
| `GET /api/cash` | Sessão, movimentos e fechamentos; admin/recebedor/fechador |
| `POST /api/cash/open` | `initial` em dinheiro; administrador |
| `POST /api/cash/movements` | `kind`, `amount`, `method`, `description`; administrador |
| `POST /api/cash/close` | `counted`; administrador ou fechador autorizado |
| `GET /api/dashboard`, `/api/reports`, `/api/reports.csv` | Indicadores e exportação; administrador |
| `GET /api/users`, `POST /api/users`, `PUT /api/users/{id}` | Equipe/permissões; administrador |
| `GET /api/audit` | Últimos 500 registros; administrador |
| `GET /api/sync/status` | Estado local, pendências e último backup |
| `POST /api/sync/ingest` | Serviço: `X-Sync-Key`, somente réplica |
| `POST /api/backup/status` | Serviço: `X-Backup-Key`, monitoramento do script |

Exemplo de fechamento (todos os campos, UUID ilustrativos; substitua pelos identificadores reais):

```json
{
  "operationId": "99bd47fa-1d1c-4da0-a89c-12cd6ff10d95",
  "payments": [{"method":"Cash","amount":25.00},{"method":"Pix","amount":25.00}],
  "customerId": null,
  "creditAmount": 0,
  "discount": 0,
  "dueAt": null
}
```

`Cash`, `Pix`, `Debit`, `Credit`, `Other` são as formas nesta versão. `Other` permite registrar outra forma, mas não há cadastro arbitrário de novas formas. Despesas: `Expense`, sangrias: `Withdrawal`, suprimentos: `Supply`. Perfil: `Admin` ou `Employee`.

Relatórios aceitam `from=AAAA-MM-DD&to=AAAA-MM-DD`, inclusivos no fuso America/Sao_Paulo, até 366 dias. O CSV é gerado pelo backend; o PDF usa impressão do navegador (Salvar como PDF), sem serviço externo. Listas de movimentos/auditoria são limitadas a 200/500 registros recentes conforme a rota; relatórios por período devem ser usados para vendas históricas.

Erros de regra retornam `{"error":"mensagem"}`. 400: entrada inválida; 401: autenticação; 403: autorização/CSRF; 404: registro inexistente; 409: conflito, estado ou integridade; 429: muitas tentativas. Uma falha de regra desfaz a transação inteira. Não há endpoints de exclusão de venda ou pagamento. `DELETE /api/products/{id}` é protegido por perfil e recusa exclusão física; desative o produto via edição.
