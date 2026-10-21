# Validação e critérios de implantação

## Executado nesta máquina Linux

| Verificação | Resultado |
|---|---|
| Build ASP.NET Core e publicação Release | Passou, sem erros |
| Migrations PostgreSQL 17 | Aplicadas em bancos reais de testes; modelo sem mudanças pendentes |
| xUnit de integração | **22 passaram, 0 falharam, 0 ignorados** |
| Build Angular de produção | Passou |
| Auditoria npm, incluindo dependências de desenvolvimento | 0 vulnerabilidades após atualização do Vite |
| Chromium / Playwright, fluxo completo e viewport de celular | **1 passou** |
| `docker compose config --quiet`, incluindo perfis remoto/backup | Passou |
| `GET /health` e consultas funcionais locais | Passaram |
| Backup age + rclone + restauração em PostgreSQL novo | Passou com destino de armazenamento de teste local |
| Build completo das imagens Docker | **Bloqueado** no download de blobs Microsoft por política de rede |
| Windows, certificado/domínio público, VPS e bucket remoto | **Não executados**; dependem de infraestrutura externa |

Os registros do runner xUnit ficam em `TestResults/acceptance.trx`, ignorado pelo Git. As falhas iniciais de configuração/seletores foram corrigidas antes das execuções aprovadas; não foram convertidas em testes ignorados.

## O que os testes de integração verificam

1. Mesa reutilizada: venda de R$ 50,00 preservada nos relatórios, mesa livre e nova comanda com UUID diferente e sem itens.
2. Fiado de R$ 100,00 com recebimento de R$ 40,00: saldo R$ 60,00, compra e pagamento preservados.
3. Estoque de 50 para 45 ao consumir cinco unidades, sem outra baixa no fechamento.
4. Funcionário recebe 403 ao alterar preços, excluir produto e consultar relatórios; custo fica oculto na leitura do catálogo.
5. Outbox persistente é reproduzido em outro banco, inclusive reenvio de cada evento, sem duplicar operações financeiras.
6. Diário confirmado é aplicado num terceiro banco novo e compara estoque/preços, clientes, comandas, vendas e pagamentos. Isso testa recuperação lógica em bancos isolados, **não perda física real de uma máquina**.
7. Duas aberturas concorrentes da mesma mesa: apenas uma é aceita.
8. Repetição de fechamento e pagamento: uma venda e uma movimentação; UUID com outro conteúdo retorna conflito.
9. Fechamento inválido desfaz a transação e não cria lacunas na sequência transacional do outbox.
10. Cancelamento de item restaura estoque e conserva auditoria.
11. Ausência de sessão, CSRF e ingestão sem chave são recusadas.
12. Limite de crédito insuficiente impede criação da dívida/venda.
13. Transporte indisponível mantém a fila; simulação de resposta perdida após commit remoto é reenviada sem duplicidade, com worker e bancos reais. O transporte HTTP é simulado nesse teste; não houve queda de uma conexão entre equipamentos físicos.
14. Pagamento misto separa recebimentos e crédito; estorno de recebimento de fiado repõe o saldo devedor com movimento inverso. Preço/custo históricos resistem à alteração posterior do produto.
15. Permissão de fechamento de caixa funciona sem conceder recebimentos ou administração, preservando esperado, contado e diferença.

Alguns cenários compartilham um teste e outros têm testes próprios; a suíte original tinha 15 cenários e a suíte atual totaliza 22 casos, não uma contagem de subtarefas.

O teste de navegador cadastra produto, entra com 50 unidades, abre caixa, cadastra mesa, atende cinco produtos de R$ 10,00, fecha R$ 50,00, confirma liberação, abre outra comanda vazia, verifica estoque 45 e navegação sem rolagem horizontal em 390 px. A execução usa servidor/API e PostgreSQL reais, sem respostas fictícias.

O teste do script de backup usou clientes PostgreSQL 17 executados no contêiner, binários age e rclone, criptografia real, upload/conferência para diretório de teste e restauração em banco novo. Isso demonstra o formato e o procedimento; não confirma disponibilidade/credenciais/retenção de um armazenamento remoto independente.

## Atualização: versão unificada para hospedagem web

O Dockerfile da raiz e `render.yaml` preparam Angular e API no mesmo endereço, com PostgreSQL gerenciado. Sete novos casos automatizados validam conversão de `DATABASE_URL`, prioridade da configuração anterior, rejeição de URLs inválidas sem exposição de credenciais e entrega do Angular real no mesmo host, mantendo cookies seguros e autorização da API. As rotas desconhecidas da API retornam 404 JSON e não a página Angular.

A publicação Release unificada foi executada nativamente com arquivos Angular compilados e conexão por `DATABASE_URL` num banco descartável. O fluxo completo de atendimento passou no Chromium apontando diretamente para essa publicação, sem servidor Angular de desenvolvimento ou proxy de frontend. Isso não é uma publicação externa: criação e validação do serviço Render ainda dependem da autorização na conta do usuário. Veja [teste web](teste-web.md).

## Etapas entregues e pendências

- **Fundação:** solução, Angular, API, banco, migration, sessão e autorização implementados.
- **Mesas/comandas:** abertura, itens, cancelamento rastreável, fechamento, reutilização, transferência/junção administrativa, balcão, divisão de pagamentos e resumo imprimível/compartilhável implementados. Atualização entre operadores por polling a cada cinco segundos, sem WebSockets.
- **Clientes/fiados:** cadastros, autorização/limite, vencimento, pagamentos parciais, saldo e histórico implementados. Extrato compartilhado pelo WhatsApp somente por ação do usuário.
- **Estoque/caixa:** movimentos, consumo sem baixa duplicada, reversões, caixa físico separado de meios eletrônicos, despesas, sangrias, suprimentos e conferência implementados.
- **Sincronização/segurança:** journal/outbox, worker, réplica com idempotência/versões, fila offline, conflitos explícitos e scripts de backup/restore implementados. **Provisionar e homologar os serviços em equipamentos independentes permanece obrigatório.**
- **Relatórios/instalação:** indicadores, período, agrupamento diário/semanal/mensal, rankings, formas/operadores, vendas, CSV e impressão/PDF pelo navegador implementados; Compose e manuais fornecidos. Imagens precisam do desbloqueio de rede e execução integral; PowerShell precisa de homologação no Windows.

Limitações funcionais explícitas: não há promoção automática/fusão entre dois escritores, SaaS multitenant, gerenciador web de downloads de backup, cadastro arbitrário de meios de pagamento ou devolução automática de venda à vista já encerrada. As formas adicionais usam `Other`; uma devolução de venda definitiva exige implementação de fluxo próprio, não apagar ou editar o histórico. Há limites de registros recentes nas telas operacionais; o CSV atual exporta vendas do período e não cada lista administrativa. Não há certificação fiscal, emissão de NFC-e, conciliação de adquirentes ou integração de pagamento Pix/cartão: registrar a forma não executa uma cobrança externa.

## Antes de usar comercialmente

1. Liberar os destinos de distribuição das imagens no ambiente de nuvem e executar o Compose completo, inclusive perfil de backup, em instalação limpa.
2. Homologar o script Windows no equipamento real, reinicialização, persistência e acesso dos dispositivos de atendimento.
3. Provisionar HTTPS e servidor remoto em outro equipamento físico; validar cookie seguro e a API sem expor PostgreSQL.
4. Cortar e restaurar a conexão real: vender localmente, confirmar a fila, reenviar, verificar ausência de duplicidades e horários no destino.
5. Validar acesso remoto de proprietário/funcionário, limitações de cada perfil e bloqueio de escritas na réplica.
6. Configurar armazenamento independente, retenção, identidade privada guardada fora do bar, monitoração e download autorizado. Testar falhas e alertas da agenda.
7. Simular perda do computador, recuperar a partir do servidor remoto/backup em outro equipamento, cercar a autoridade antiga e comparar vendas, pagamentos, clientes, estoque e comandas. Documentar o ponto de recuperação e a perda possível de pendências.
8. Revisar os fluxos ainda limitados acima, regras operacionais, permissões, política LGPD e requisitos fiscais com o proprietário. Corrigir qualquer falha crítica encontrada antes da liberação.

Os testes locais aprovados não autorizam chamar o sistema de pronto para produção. A workflow CI foi incluída no repositório, mas sua execução no GitHub não foi disparada nesta tarefa.
