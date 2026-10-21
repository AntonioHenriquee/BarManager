#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
[ ! -e .env ] || { echo '.env já existe; arquivo preservado.'; exit 0; }
umask 077
python3 - <<'PY'
import pathlib,secrets
text=pathlib.Path('.env.example').read_text()
for name in ['DB_PASSWORD','ADMIN_PASSWORD','SYNC_KEY','BACKUP_STATUS_KEY']:
    text=text.replace(name+'=\n',name+'='+secrets.token_urlsafe(36)+'\n')
with open('.env','x') as f:f.write(text)
print('.env criado com segredos aleatórios. Consulte ADMIN_PASSWORD localmente para o primeiro acesso (usuário admin).')
PY
