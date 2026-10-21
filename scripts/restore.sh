#!/bin/sh
# Restaura SOMENTE em banco novo/vazio; nunca remove os dados existentes.
set -eu
umask 077
[ "$#" -eq 2 ] || { echo 'Uso: scripts/restore.sh arquivo.dump.age /caminho/identidade-age.txt' >&2; exit 1; }
: "${PGHOST:?Informe o PostgreSQL de destino novo}"
: "${PGDATABASE:?Informe o banco de destino novo}"
: "${PGUSER:?Informe o usuário do destino}"
count=$(psql -X -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")
[ "$count" = 0 ] || { echo 'Restauração recusada: destino não está vazio.' >&2; exit 1; }
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
age --decrypt -i "$2" -o "$work/database.dump" "$1"
pg_restore --single-transaction --exit-on-error --no-owner --no-acl --dbname="$PGDATABASE" "$work/database.dump"
psql -X -v ON_ERROR_STOP=1 -c 'SELECT count(*) AS vendas, coalesce(sum("Total"),0) AS total FROM "Sales";' -c 'SELECT count(*) AS pagamentos, coalesce(sum("Amount"),0) AS total FROM "Payments";'
echo 'Dados restaurados. Valide estoque, clientes e comandas antes de permitir novas operações.'
