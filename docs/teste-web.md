# Testar na web sem instalar nada

A versão unificada entrega o Angular e a API ASP.NET Core no mesmo endereço HTTPS, com PostgreSQL separado. O `Dockerfile` da raiz gera a aplicação completa. O arquivo `render.yaml` prepara um Blueprint no Render para uma demonstração de um estabelecimento.

## Publicar pelo navegador

1. Acesse o botão **Deploy to Render** no README e entre/crie sua conta Render. Autorize o acesso ao repositório `AntonioHenriquee/BarManager` e à branch `feat/meu-bar-web`.
2. Revise o Blueprint: um serviço web `meu-bar-teste` e um PostgreSQL `meu-bar-teste-db`, ambos com `plan: free`. Confirme que sua conta oferece esses planos e confira as condições no painel. Não selecione um plano pago sem querer.
3. Confirme a criação e aguarde o build e a verificação `/health`. O Render fornecerá o endereço público HTTPS do serviço. Esse endereço só existe depois de a criação concluir; o repositório por si só não publica a aplicação.
4. Abra **Environment** do serviço web no painel Render e consulte o valor gerado para `Bootstrap__AdminPassword`. Esse segredo é criado na sua conta, não está no GitHub. Entre no endereço público com usuário **admin** e essa senha; altere-a em Funcionários após o primeiro acesso.
5. Cadastre produtos e entradas de estoque, abra o caixa e experimente Mesas e Balcão. A demonstração usa banco real e não possui vendas ou dados pessoais fictícios pré-carregados.

**Deploy to Render:** https://render.com/deploy?repo=https%3A%2F%2Fgithub.com%2FAntonioHenriquee%2FBarManager&branch=feat%2Fmeu-bar-web

Se o assistente não selecionar a branch automaticamente, use **New → Blueprint**, selecione o repositório e escolha explicitamente `feat/meu-bar-web`. Se o repositório for privado, conceda acesso a ele à integração Render/GitHub na sua própria conta.

## Limites desta demonstração

- Esta configuração é para testes com dados descartáveis. Não a use para atendimento real ou para dados pessoais/financeiros reais.
- Disponibilidade, suspensão por inatividade, cotas e expiração de banco gratuito seguem as condições exibidas pelo Render. A primeira abertura pode demorar após suspensão. A disponibilidade de um plano gratuito não foi verificada na sua conta.
- A base fica no serviço PostgreSQL; o filesystem do serviço web pode ser substituído em redeploys. Não salve backups somente nele.
- A demonstração não configura a arquitetura híbrida, uma réplica independente ou armazenamento de backup remoto. Para operação comercial, siga os outros manuais e os critérios de aceitação.
- A autorização e criação na conta Render não podem ser feitas por este chat sem uma integração/credencial dessa hospedagem. Nenhuma hospedagem pública foi criada automaticamente só por adicionar os arquivos.
- Após o teste, você pode excluir o Blueprint e o banco pelo painel da sua conta, caso não queira mantê-los.

## Outras hospedagens

Use o Dockerfile da raiz em uma plataforma com suporte a Docker/.NET 10 e PostgreSQL. Configure `DATABASE_URL` no formato PostgreSQL (`postgres://...` ou `postgresql://...`) ou `ConnectionStrings__Database` no formato Npgsql; a conexão Npgsql explícita tem prioridade. Opções `sslmode` e `application_name` na URL são respeitadas; opções desconhecidas geram erro em vez de serem descartadas silenciosamente.

Configure `Database__Migrate=true`, `Bootstrap__AdminPassword` com pelo menos 12 caracteres, `Security__SecureCookies=true`, `Node__Mode=Authority`, `Node__DeviceId` estável e `ASPNETCORE_HTTP_PORTS=8080`. O proxy da hospedagem deve terminar HTTPS e encaminhar para a porta 8080. Não publique o PostgreSQL nem os segredos. A senha inicial só é usada em banco sem usuários; mudá-la no painel depois não redefine a senha de uma conta já existente.
