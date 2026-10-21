#!/bin/sh
set -eu
umask 077
: "${AGE_RECIPIENT:?Configure a chave pública age}"
: "${BACKUP_REMOTE:?Configure o destino remoto rclone}"
: "${BACKUP_STATUS_KEY:?Configure a chave de monitoramento}"
: "${STATUS_URL:?Configure a URL de monitoramento}"
case "${RETENTION_DAYS:-30}" in ''|*[!0-9]*|0) echo 'Retenção inválida' >&2; exit 1;; esac
work=$(mktemp -d)
notify() {
 curl --fail --silent --show-error --max-time 20 -H "X-Backup-Key: $BACKUP_STATUS_KEY" -H 'Content-Type: application/json' -d "$1" "$STATUS_URL" >/dev/null
}
cleanup() { code=$?; trap - EXIT; rm -rf "$work"; if [ "$code" -ne 0 ]; then notify '{"success":false,"message":"Backup falhou. Consulte o serviço de backup; dados remotos não confirmados."}' || true; fi; exit "$code"; }
trap cleanup EXIT
filename="meubar-$(date -u +%Y%m%dT%H%M%SZ).dump.age"
pg_dump --format=custom --no-owner --no-acl --file="$work/database.dump"
age -r "$AGE_RECIPIENT" -o "$work/$filename" "$work/database.dump"
rm "$work/database.dump"
rclone copyto "$work/$filename" "${BACKUP_REMOTE%/}/$filename" --checksum
# Confirma a presença e conteúdo remoto; não assume sucesso apenas pelo processo iniciado.
rclone check "$work" "${BACKUP_REMOTE%/}" --one-way --download
rclone delete "${BACKUP_REMOTE%/}" --min-age "${RETENTION_DAYS:-30}d" --include 'meubar-*.dump.age'
notify '{"success":true,"message":"Backup criptografado enviado e conferido no armazenamento remoto."}'
echo "Backup confirmado: $filename"
