# Acesso remoto e réplica independente

## Dois destinos diferentes

- **Autoridade local**: computador do estabelecimento. É o único nó que grava operações do bar. Continua atendendo pela rede local sem internet.
- **Réplica remota**: VPS/servidor em outro equipamento físico, com PostgreSQL persistente. Recebe o journal transacional e permite consultas com os mesmos usuários. Comandos de negócio são recusados nesse nó.

Atendimento remoto por celular acessa a **autoridade** por HTTPS, para operar o mesmo caixa e estoque. Consultas de contingência podem acessar a réplica, considerando a última sincronização. Esta versão não permite vendas concorrentes em duas autoridades independentes. Se o computador local estiver indisponível, execute uma recuperação controlada antes de voltar a vender.

## HTTPS da autoridade

Obtenha um domínio e direcione DNS para seu acesso público. Configure no `.env` da autoridade:

```dotenv
PUBLIC_DOMAIN=bar.seudominio.example
SECURE_COOKIES=true
NODE_MODE=Authority
DEVICE_ID=bar-autoridade-01
```

Execute `docker compose --profile remote up -d --build`. O Caddy termina HTTPS e encaminha apenas para `web`, que encaminha `/api` para a API. Libere somente TCP 80/443 e, opcionalmente, UDP 443. Preserve os volumes Caddy para renovação dos certificados. A rede precisa permitir os desafios ACME e a renovação automática.

Se o provedor usa CGNAT ou não permite conexões de entrada, use um túnel HTTPS autenticado/VPN de acesso com servidor confiável. Configure o proxy para chegar ao serviço web, preserve cookies seguros e revise quem pode acessar o túnel. Não instale VPN dentro dos contêineres nem abra o banco de dados. A provisão de domínio, certificado público e túnel depende da infraestrutura escolhida e não foi executada aqui.

## Instalar réplica em outro equipamento

1. Em uma máquina remota separada, instale Docker/Compose e o mesmo código. Crie `.env` próprio, com **outra senha de banco**.
2. Use um banco vazio e `NODE_MODE=Replica`. Não inicialize esse banco antes como Authority: isso criaria usuários/eventos incompatíveis. O bootstrap de administrador é ignorado em réplica.
3. Configure `AUTHORITY_DEVICE_ID` exatamente igual ao `DEVICE_ID` da autoridade. `DEVICE_ID` do próprio nó remoto pode ser outro.
4. Gere uma chave aleatória com pelo menos 32 caracteres para `SYNC_KEY`; configure a mesma chave somente nesses dois nós. Nunca a publique. Não reutilize senha do usuário ou chave de backup.
5. Configure domínio HTTPS próprio da réplica, `PUBLIC_DOMAIN=replica.seudominio.example` e `SECURE_COOKIES=true`. Inicie com o perfil `remote` e confirme `/health` por HTTPS.
6. Na autoridade, configure `SYNC_REMOTE_URL=https://replica.seudominio.example` e a mesma `SYNC_KEY`; recrie a API. Não use HTTP, URL da própria autoridade ou endereço do banco.
7. Observe **Sincronização e Backup**. O worker tenta a cada 15 segundos, envia até 50 envelopes por ciclo e marca cada confirmação individualmente. Compare vendas/pagamentos no destino e espere pendências chegarem a zero.
8. Restrinja a rota de ingestão à rede/origem da autoridade quando possível, além da chave de serviço. Mantenha portas de administração protegidas e atualizações do host em dia.

Usuários e hashes são replicados; sessões não. O primeiro login na réplica estará disponível depois da replicação dos usuários. O banco da réplica nunca é exposto à internet.

## Queda de conexão e conflitos

Interrompa temporariamente apenas a comunicação com a réplica, registre uma venda local e confirme que ela entrou no outbox. Restabeleça a conexão, acompanhe a confirmação e verifique uma única venda e um único recebimento remoto. O teste automatizado cobre também resposta perdida **após** commit remoto; a retransmissão não duplica registros.

Se houver 409, não limpe a fila nem edite versões para forçar o aceite. Inspecione o evento, dispositivo, sequência e estado do destino. Uma réplica inicialmente populada por outra autoridade deve ser substituída por um banco vazio ou por um backup consistente da mesma linha histórica. A aplicação interrompe o envio para não ocultar divergências.

Uma confirmação de health não substitui a confirmação do envelope. A aplicação mantém o número de operações não confirmadas. **A recuperação alcança apenas os dados efetivamente replicados ou presentes no backup disponível.**

## Rede do ambiente de desenvolvimento na nuvem

O build Docker precisa acessar os registros de imagens e suas distribuições. Nesta execução, o registry Microsoft respondeu, mas seus blobs em `centralus.data.mcr.microsoft.com` foram bloqueados por política de rede. Foram salvas as adições `*.data.mcr.microsoft.com` e `dl-cdn.alpinelinux.org` no rascunho de configuração. Isso não aplica nem publica a alteração. Revise/salve nas configurações do ambiente e publique; então repita `docker compose build`. O build nativo da API e o build de produção Angular foram executados independentemente.
