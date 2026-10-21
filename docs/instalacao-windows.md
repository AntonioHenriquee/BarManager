# Instalação no Windows

## Pré-requisitos

Windows 10/11 compatível com Docker Desktop, virtualização habilitada, WSL2 instalado, Docker Desktop iniciado no modo de contêineres Linux e Git. O primeiro build precisa de internet para obter imagens e pacotes. Depois de iniciado, o atendimento local não exige internet; sincronização, backup remoto e acesso de fora exigem conectividade.

1. Clone este repositório em uma pasta de trabalho do seu usuário.
2. No PowerShell, entre nessa pasta e execute `./scripts/start.ps1`. Se a política da sua máquina impedir scripts, revise a política com o responsável pela máquina; também pode gerar `.env` manualmente a partir do exemplo e executar `docker compose up -d --build`.
3. O script cria `.env` com senhas aleatórias somente se ele ainda não existir. Guarde esse arquivo com acesso limitado ao proprietário. Leia `ADMIN_PASSWORD` localmente; não publique o conteúdo.
4. Aguarde `docker compose ps` e confirme que `Invoke-RestMethod http://127.0.0.1:8080/health` retorna `status: ok` (substitua a porta se alterou `LOCAL_PORT`).
5. Abra o endereço local no navegador, entre como `admin` com a senha configurada e altere a senha em Funcionários.
6. Cadastre produtos reais, faça as entradas iniciais, configure mesas e abra o caixa. Não há catálogo de demonstração na instalação.

Use `docker compose logs --tail=100 api web` para diagnóstico. Não envie `.env`, logs com dados pessoais ou arquivos de backup sem proteção em chamados públicos.

## Persistência e reinicialização

Os serviços usam `restart: unless-stopped`; configure o Docker Desktop para iniciar junto com a sessão do Windows. Após reiniciar, aguarde o Docker e confirme `/health` antes de atender. Fechar o navegador não encerra o servidor. Para uma parada controlada, execute `docker compose stop`; para voltar, `docker compose up -d`.

Não execute `docker compose down -v` em uma instalação com dados. Não faça cópias do diretório de dados PostgreSQL com o banco em uso; utilize `pg_dump` e o procedimento de backup.

Se aparecer erro de porta ocupada, altere somente `LOCAL_PORT` em `.env` e recrie `web`. O PostgreSQL não publica porta na instalação padrão. Não crie uma regra de firewall expondo-o.

## Atualizações

Agende uma janela, confirme o outbox sincronizado e faça um backup verificado. Preserve a versão anterior do código/imagens. Pare os operadores, obtenha a versão revisada e execute `docker compose up -d --build`. A API aplica migrations versionadas ao iniciar. Revise migrations antes de atualizar dados reais. Se uma migration falhar, interrompa os atendimentos e recupere o backup em banco novo; não faça rollback destrutivo improvisado.

## Acesso de celulares e internet

A configuração inicial atende somente loopback. Não altere o bind para acesso público sem configurar HTTPS. Siga [acesso remoto](acesso-remoto.md) para domínio, certificado, autoridade e réplica em outro equipamento. Não exponha `ng serve` ou a porta interna da API.

## Proteção contra perda do computador

Instalação local concluída não significa recuperação garantida. Configure a réplica em equipamento independente e backup remoto criptografado, acompanhe pendências e faça a simulação de recuperação do [manual](backup-recuperacao.md). Operações ainda não enviadas podem ser perdidas em caso de furto ou falha do disco.

O script PowerShell foi fornecido, mas sua execução em um Windows real não foi validada neste ambiente Linux. Essa verificação permanece no roteiro de homologação.
