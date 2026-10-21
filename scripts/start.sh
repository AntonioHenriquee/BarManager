#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
[ -f .env ] || { echo 'Execute scripts/init-env.sh primeiro.' >&2; exit 1; }
docker compose up -d --build
endpoint=$(docker compose port web 8080)
printf 'Aguardando o sistema'
i=0
until curl --fail --silent "http://$endpoint/health" >/dev/null; do
 i=$((i+1)); [ "$i" -lt 60 ] || { echo 'Verifique docker compose logs api web'; exit 1; }; printf '.'; sleep 2
done
printf '\nSistema iniciado na porta local configurada em .env.\n'
