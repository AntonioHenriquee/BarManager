# Backup e recuperação

## O que está protegido

Sincronização é transacional, não uma cópia agendada do banco. Backup é uma segunda proteção versionada contra erro operacional, corrupção ou perda do servidor. Use banco remoto em outro equipamento e armazenamento de backup independente do local e do servidor remoto, preferencialmente com retenção imutável no provedor.

Não existe serviço remoto embutido ou conta de armazenamento fornecida pelo projeto. Configure seus destinos e credenciais. Enquanto isso não for feito e testado, o painel informa ausência de proteção confirmada. Nunca prometa recuperar operações pendentes que não saíram do computador perdido.

## Configurar backups automáticos

1. Gere uma identidade age em máquina segura: `age-keygen -o identidade-proprietario.txt`. Guarde **a chave privada fora do computador do bar**, em cofre seguro com uma segunda cópia. Não a coloque no serviço de backup. Sem ela, o backup criptografado não pode ser restaurado.
2. Copie somente a chave pública `age1...` para `AGE_RECIPIENT` no `.env`.
3. Configure um destino rclone suportado (S3, B2, outro servidor etc.) com credenciais restritas ao prefixo do estabelecimento. Salve a configuração em `.local/rclone.conf`, ignorada pelo Git. O arquivo é montado somente para leitura no contêiner. Como o serviço roda com usuário não privilegiado, ajuste grupo/permissões do arquivo para permitir a leitura por UID/GID 65534 sem torná-lo acessível a outros usuários do host; valide o acesso antes de ativar a agenda.
4. Defina `BACKUP_REMOTE=nome-remoto:bucket/prefixo-exclusivo`, `RETENTION_DAYS=30`, `BACKUP_INTERVAL_SECONDS=3600` e `BACKUP_STATUS_KEY` aleatória de pelo menos 32 caracteres. Não use o mesmo prefixo para dois estabelecimentos ou agendas concorrentes.
5. Inicie `docker compose --profile backup up -d --build`. Para executar uma rodada manual: `docker compose --profile backup exec backup /usr/local/bin/backup` (não rode simultaneamente com a rodada automática).
6. Confira `docker compose logs backup`, a presença do arquivo remoto e o status do painel. Execute uma restauração de teste antes de confiar na agenda.

O script usa `pg_dump` consistente, criptografa com age, remove a cópia temporária em claro, envia com rclone e confere o conteúdo remoto por download. Só então remove arquivos antigos que combinam com `meubar-*.dump.age`, respeitando a retenção em dias, e registra sucesso na API. Falhas registram um evento de falha quando a API está acessível. Se nem a API estiver acessível, o processo retorna erro e os logs do serviço registram a falha; monitore também externamente. Proteja o volume temporário/host e use criptografia de disco para reduzir exposição do dump durante a geração.

O histórico de execuções fica em `BackupRuns`; o painel mostra a última execução. Esta versão não oferece um gerenciador de downloads na interface: obtenha backups somente por acesso autenticado ao armazenamento/servidor com autorização do proprietário. Prefira permissão de escrita separada da permissão de download/exclusão e configure retenção/alertas externos. Quem controla as credenciais de exclusão pode remover backups; imutabilidade do provedor fortalece essa proteção.

## Restaurar em banco novo

Requer clientes PostgreSQL 17, age e o script `scripts/restore.sh` em Linux/macOS, WSL2 ou contêiner de manutenção. Nunca restaure sobre o banco de produção ativo.

1. Pare atendimentos e registre o ponto de recuperação esperado (hora e última sequência confirmada).
2. Baixe um arquivo `.dump.age` autorizado para área temporária privada. Disponibilize a identidade age por canal seguro, fora do repositório.
3. Crie um PostgreSQL vazio e isolado. Configure `PGHOST`, `PGPORT` se necessário, `PGDATABASE`, `PGUSER` e autenticação segura do cliente (arquivo de senha com permissões restritas ou variável do processo). Não use senha na linha de comando/histórico.
4. Execute `scripts/restore.sh /caminho/arquivo.dump.age /caminho/identidade.txt`. O script recusa banco com tabelas existentes, descriptografa temporariamente e executa `pg_restore` em uma única transação com erro interrompendo a operação.
5. Confira contagens e somatórios de vendas e pagamentos, saldos de clientes, estoque, comandas e última sequência. Compare com as referências do servidor/backup. Teste login e o fluxo completo em cópia de homologação.
6. Apague a cópia temporária do backup e remova a identidade de recuperação da máquina operacional. Registre responsável, arquivo, data, verificação e eventual perda conhecida num registro operacional protegido.

Restauração criptográfica e comparação em um banco novo foram executadas aqui com armazenamento de teste local. Isso não valida seu bucket, credenciais, retenção externa ou recuperação em outro computador real.

## Furto/perda total da autoridade local

1. Desabilite acesso do equipamento antigo, inclusive túnel, chaves de serviço e contas do host. **Não promova enquanto houver possibilidade de duas autoridades operarem.** Registre a perda potencial de operações que estavam pendentes.
2. Pare a ingestão da réplica num ponto conhecido e faça uma exportação consistente do banco remoto, ou obtenha seu backup remoto mais recente. Prefira a exportação atual da réplica se ela contém confirmações posteriores ao backup. Registre a sequência de `ReplicaStates` e do último envelope em `Outbox`.
3. Instale em novo computador e restaure num banco vazio antes de iniciar a API. Uma cópia completa preserva UUIDs, versões, idempotência e journal. Não misture dados com um bootstrap criado por outra autoridade.
4. Com todas as APIs paradas, elimine sessões antigas e ajuste a sequência numérica de comandas, pois a réplica recebeu números explícitos:

```sql
DELETE FROM "Sessions";
SELECT setval(pg_get_serial_sequence('"Tabs"','Number'),
              COALESCE((SELECT MAX("Number") FROM "Tabs"),1),
              EXISTS(SELECT 1 FROM "Tabs"));
```

5. Configure o novo nó como `Authority`, mantendo o `DEVICE_ID` da linha histórica recuperada. Troque senhas de administrador e de banco conforme o incidente, proteja `.env` e gere novas chaves de sincronização/backup. Não devolva o nó antigo à operação.
6. Antes de usar uma réplica já existente, confirme que ela está exatamente no ponto de recuperação. Se estiver mais adiantada, **não retroceda a autoridade**: refaça a exportação a partir do ponto mais recente consistente. Se iniciar uma réplica nova/vazia, reenvie o journal inteiro marcando os envelopes como pendentes somente no novo nó, em janela de manutenção:

```sql
UPDATE "Outbox" SET "SentAt" = NULL;
```

7. Inicie a nova autoridade, valide login, consultas, valores, estoque e uma operação controlada. Configure a réplica independente, espere a fila esvaziar e valide backup/restore. Só então libere o atendimento.

Não há promoção automática, lease distribuído ou fusão de escritas entre duas autoridades offline. O isolamento do equipamento antigo é uma ação operacional obrigatória. Se isso não puder ser garantido, suspenda a promoção e faça reconciliação assistida; as verificações de versão detectam divergências, mas não recuperam automaticamente dinheiro/estoque de histórias distintas.
