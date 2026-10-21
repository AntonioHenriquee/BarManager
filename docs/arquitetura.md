# Decisões de arquitetura

## Organização

- `backend/MeuBar.Api/Domain.cs`: entidades e restrições EF/PostgreSQL.
- `Contracts.cs`: contratos de comandos; `Operations.cs`: regras de atendimento, estoque e pagamentos.
- `Infrastructure.cs`: sessão autenticada, transação idempotente, diário e transporte de sincronização.
- `Program.cs`: composição, autorização e endpoints REST.
- `Reports.cs`: leituras financeiras, agrupamentos e CSV.
- `Migrations/`: schema versionado; `backend/MeuBar.Tests/`: testes de integração com PostgreSQL real.
- `frontend/src`: aplicação Angular, serviço HTTP e interface responsiva. Dados exibidos vêm da API.
- `deploy/`, `compose.yaml` e `scripts/`: instalação, proxy HTTPS e backup.

A instalação representa **um estabelecimento**, com uma sessão de caixa global aberta por vez e operadores identificados. Não é um SaaS multitenant: instale bancos e serviços separados para estabelecimentos distintos. Não criamos uma tabela de perfil ou categoria apenas para representar dois valores estáticos ou um texto; perfis são validados e categorias são campos de produto. Essa simplificação evita estrutura sem uso real.

## Comandas e estoque

Mesa é um ponto de atendimento fixo; comanda tem UUID global e número sequencial próprio. O índice único parcial de `Tabs.TableId` admite apenas uma comanda `Open`/`Closing` por mesa. Uma nova ocupação cria outra comanda sem reaproveitar seus itens. Vendas anteriores continuam referenciando a comanda encerrada.

No lançamento, o item recebe cópias do nome, preço de venda e custo. Alterar o cadastro depois não altera o histórico. Estoque é reservado/consumido no lançamento; fechar não repete a baixa. Cancelar item/comanda exige administrador e motivo, mantém os registros e gera movimento inverso. Transferências e junções exigem administrador e são transacionais. Junções preservam o encerramento da comanda de origem e os itens são vinculados à comanda de destino sem movimentar estoque novamente.

A tela de atendimento consulta automaticamente comandas e totais a cada cinco segundos enquanto não há formulário em edição; o operador também recebe o resultado imediato dos próprios lançamentos. Esta versão usa polling, não WebSockets. Um fechamento concorrente remove a comanda da visão operacional; validações transacionais continuam obrigatórias no servidor.

## Valores e caixa

C# `decimal` e PostgreSQL `numeric(18,2)` para valores; quantidades/estoque `numeric(18,3)`. Entradas com precisão excedente são recusadas. Totais dos itens são arredondados para centavos com `MidpointRounding.AwayFromZero`. Preços e custos são instantâneos do lançamento.

Uma venda é reconhecida no fechamento. Pagamentos mais crédito devem coincidir exatamente com o total menos desconto. Apenas administrador concede desconto. Fiado cria conta a receber e não cria entrada de caixa. Recebimento de fiado cria pagamento e movimento no momento de seu recebimento. O saldo devedor diminui, mas a obrigação original e os pagamentos permanecem.

Saldo físico esperado = saldo inicial + movimentos **em dinheiro**. Pix/débito/crédito/outros têm movimentos separados e não entram na gaveta de dinheiro. Ao fechar, ficam registrados esperado, contado, diferença, data e operador. Lucro bruto estimado = venda finalizada menos custos registrados nos itens; não representa lucro líquido.

O estorno implementado é de recebimento de fiado: reabre o saldo correspondente e lança movimento inverso no caixa atualmente aberto. Devoluções/estornos de vendas à vista encerradas não são automatizados; a API recusa esse fluxo para evitar reabrir obrigações silenciosamente. Não existe exclusão física de vendas ou produtos históricos.

## Transações e concorrência

Todo comando de negócio usa uma transação PostgreSQL e `pg_advisory_xact_lock` comum ao estabelecimento. Isso serializa escritas, inclusive entre processos, de forma simples e adequada ao volume de um bar. Restrições de unicidade, checks e chaves estrangeiras oferecem proteção adicional. Para escalar para grandes volumes, substitua o bloqueio global por bloqueios agregados, mantendo as invariantes.

Todo comando recebe `operationId` UUID, persistido com usuário, tipo, hash do pedido e resposta. Repetição do mesmo pedido retorna a resposta anterior; reutilizar o identificador com outro conteúdo/usuário retorna 409. Uma nova requisição com outro UUID não é considerada repetição da anterior. O cliente evita cliques paralelos e mantém o UUID durante a tentativa corrente; depois de um erro de comunicação consulte o histórico antes de abrir uma nova operação.

Registros têm `Version` como token de concorrência. Cadastros editáveis exigem a versão lida. Preços e quantidades não são aceitos do navegador no fechamento: o servidor usa os itens persistidos e valida limites/crédito.

## Sincronização e autoridade

Foi escolhida **uma única autoridade de escrita** por estabelecimento. Não existe fusão automática de duas autoridades financeiras independentes. Atendimento remoto acessa a autoridade por HTTPS; a réplica remota oferece consulta e recuperação. Se a internet cair, a autoridade local continua funcionando. A réplica não aceita comandos financeiros/cadastrais, mesmo de administrador.

Cada transação grava, no mesmo commit, um envelope no outbox com alterações versionadas, UUID e sequência sem lacunas. A sequência do outbox é alocada sob o bloqueio transacional, sem usar sequences PostgreSQL (que podem consumir números em rollback). A réplica aplica o envelope inteiro em uma transação, com ordem, dispositivo autorizado e versões anteriores obrigatórias. Sequência repetida com outro conteúdo, lacuna ou versão inesperada provoca 409; o envio para nessa operação. Nenhum evento conflitante é descartado ou sobrescrito.

O worker envia lotes ordenados e só marca a operação como transmitida depois do ACK. Se a confirmação se perder depois do commit remoto, a repetição é reconhecida no journal. Sessões de login e metadados locais de saúde não são replicados; usuários e hashes de senha são replicados para permitir autenticação na recuperação. Transporte exige HTTPS e chave de serviço separada com pelo menos 32 caracteres. O journal é mantido integralmente para permitir construir uma réplica vazia; não há compactação automática nesta versão.

A edição bidirecional de finanças está fora do modelo de autoridade escolhido. Não configure dois nós `Authority` para o mesmo estabelecimento. Uma promoção exige retirada efetiva do equipamento antigo e rotação das chaves, conforme o manual. Caso o equipamento antigo volte a escrever indevidamente, os conflitos são recusados na sincronização e exigem reconciliação humana; o software não resolve esse cenário sozinho.

## Segurança e dados pessoais

Senhas usam `PasswordHasher` do ASP.NET Identity (PBKDF2 com salt). Sessões usam token aleatório de 256 bits, somente o hash no banco, cookie HttpOnly/SameSite Strict e expiração absoluta de 8 horas. Usuário ativo e permissões são relidos a cada requisição. Não há token em localStorage.

Mutações de navegador exigem `X-MeuBar: 1`; sem CORS permissivo. Rotas de serviço usam chaves próprias, comparação constante e limite de tamanho de corpo. Há limitação de tentativas de login por IP do proxy recebido. O proxy de exemplo não confia em cabeçalhos de cliente arbitrários; atrás dele o limite pode ser compartilhado pelos operadores. Configurar limites por cliente requer revisar a cadeia de proxies confiáveis antes de usar forwarded headers.

Os logs não incluem senhas, tokens ou payloads de login. Auditoria de negócio mantém responsável, ação, data e identificador da operação; os registros financeiros e o journal permitem rastrear as alterações. Não há exclusão em cascata de histórico financeiro. Backups criptografados e credenciais de acesso devem seguir política de acesso restrito. Defina responsável pelo tratamento, base legal, prazo de retenção e procedimento de atendimento aos titulares conforme o contexto do estabelecimento; o software não determina sua política legal.
