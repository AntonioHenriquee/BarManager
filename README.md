# MEU BAR

Sistema web em português para mesas e balcão, comandas reutilizáveis, fiados, estoque e caixa. Frontend Angular 22 / TypeScript / Bootstrap 5, API ASP.NET Core 10 / EF Core e PostgreSQL 17. Aplicação acessível pelo navegador; não contém APK.

**Estado: implementação funcional para desenvolvimento e homologação. Não está liberada para uso comercial.** Os fluxos locais são executáveis e testados. A infraestrutura remota precisa ser configurada e os ensaios entre equipamentos independentes precisam passar antes de usar dados reais. Veja [aceitação e limitações](docs/aceitacao.md).

## Testar pela web, sem instalar

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https%3A%2F%2Fgithub.com%2FAntonioHenriquee%2FBarManager&branch=feat%2Fmeu-bar-web)

O botão prepara frontend, API e banco na sua conta Render. Autorize a integração, selecione a branch `feat/meu-bar-web` e confirme os planos disponíveis. O link público será fornecido pelo Render após o deploy; esta configuração não equivale a um site já publicado. Usuário `admin`; a senha é gerada no painel, em `Bootstrap__AdminPassword`. [Passo a passo da demonstração](docs/teste-web.md).

## Iniciar com Docker

Instale Docker com Compose v2. Em Linux/macOS com Python 3 e curl:

```sh
./scripts/init-env.sh
./scripts/start.sh
```

No Windows, instale Docker Desktop com WSL2 e execute `./scripts/start.ps1` no PowerShell. Veja o [manual Windows](docs/instalacao-windows.md).

O script gera `.env` com senhas aleatórias e preserva um arquivo existente. Acesse a porta de loopback configurada em `LOCAL_PORT` (8080 por padrão) pelo navegador. Usuário inicial: `admin`; a senha está em `ADMIN_PASSWORD` no seu arquivo local. Não há senha padrão compartilhada. Proteja esse arquivo; não o envie em mensagens ou commits. Depois do primeiro acesso, altere a senha em **Funcionários**. A variável de bootstrap é usada somente quando o banco ainda não tem usuários.

O banco fica em volume Docker persistente e não publica portas. `docker compose down` preserva o volume; **`docker compose down -v` apaga os dados**. Um volume local não protege contra perda do equipamento.

## Primeiro atendimento

1. Em **Produtos**, cadastre produtos e use **Estoque** para registrar a entrada inicial.
2. Em **Caixa**, abra uma sessão com o saldo físico em dinheiro.
3. Em **Mesas e Balcão**, cadastre mesas. Abra uma comanda ou use **Venda rápida**.
4. Adicione produtos. O estoque é descontado nesse momento, uma única vez.
5. Feche distribuindo exatamente o valor entre pagamentos e eventual fiado autorizado. O fechamento libera a mesa e preserva venda, itens e pagamentos.
6. Para fiados, cadastre o cliente, autorize o crédito e defina o limite. Recebimentos parciais são feitos em **Fiados**; a dívida original não é apagada.

Funcionários podem atender e solicitar fechamento, mas não administram preços, clientes, estoque ou relatórios. O administrador pode conceder recebimentos e fechamento de caixa separadamente. Restrições são verificadas na API, inclusive em chamadas diretas.

## Desenvolvimento sem Docker para API/frontend

Requer SDK .NET 10, Node 24.15+ (compatível com Angular 22), npm e PostgreSQL 17. `global.json`, manifests, ferramentas e lockfiles estão versionados. Execute na raiz:

```sh
dotnet tool restore
dotnet restore MeuBar.slnx --locked-mode
npm --prefix frontend ci
```

Configure no processo da API, de forma segura:

- `ConnectionStrings__Database`: conexão PostgreSQL de desenvolvimento.
- `Database__Migrate=true`: aplicar migrations no início (use uma única instância para migrar).
- `Bootstrap__AdminPassword`: senha inicial de 12 a 128 caracteres.
- `Security__SecureCookies=false`: exclusivamente em HTTP local; HTTPS remoto exige `true`.
- `ASPNETCORE_ENVIRONMENT=Development` e `ASPNETCORE_URLS=http://127.0.0.1:5080`.
- `Node__Mode=Authority` e um `Node__DeviceId` estável e exclusivo do estabelecimento.

```sh
dotnet run --project backend/MeuBar.Api
# outro terminal
npm --prefix frontend start
```

O servidor Angular encaminha `/api` para a API local na porta 5080. Não exponha o servidor de desenvolvimento na internet. Verificação interna: `GET /health` deve retornar `{"status":"ok"}`. Faça login e execute o atendimento acima para verificar o fluxo funcional. A API não depende de dados fictícios nem inicializa produtos, clientes ou vendas de demonstração.

## Build e testes

```sh
npm --prefix frontend run build
dotnet build MeuBar.slnx --no-restore
# Conexão exclusiva de testes, com permissão CREATE DATABASE:
# export MEUBAR_TEST_DATABASE='Host=...;Database=postgres;Username=...;Password=...'
dotnet test backend/MeuBar.Tests
# Aplicação iniciada contra um banco descartável; forneça senha pelo ambiente:
# export MEUBAR_E2E_PASSWORD=...
# export CHROMIUM_PATH=/caminho/para/chromium
npm --prefix frontend run test:e2e
```

O teste de publicação unificada exige o frontend compilado: execute `npm --prefix frontend run build` antes de `dotnet test`. Os testes xUnit criam bancos de nomes aleatórios, executam migrations e removem apenas seus bancos ao terminar. Os testes de navegador criam registros com prefixo `E2E-`; **use banco descartável**. É possível instalar o navegador com `npx playwright install chromium` e indicar `CHROMIUM_PATH`, ou usar o Chromium instalado no sistema. A configuração padrão de testes do contêiner usa `/usr/bin/chromium` com sandbox desativada; execute apenas em ambiente isolado de teste.

O frontend usa Vite 8.3.5 por override explícito para corrigir os avisos de segurança presentes na versão transitiva anterior de `@angular/build`.

## Documentação

- [Arquitetura e regras financeiras](docs/arquitetura.md)
- [API e autenticação](docs/api.md)
- [Instalação no Windows](docs/instalacao-windows.md)
- [Acesso remoto e réplica](docs/acesso-remoto.md)
- [Backup, restauração e perda do equipamento](docs/backup-recuperacao.md)
- [Testes executados e critérios de implantação](docs/aceitacao.md)

Mantenha domínio, HTTPS, servidor remoto, credenciais e chaves de backup fora do código-fonte. Sincronização não recupera operações ainda não transmitidas. O painel exibe essa condição explicitamente.
