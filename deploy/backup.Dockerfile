FROM alpine:3.22
RUN apk add --no-cache postgresql17-client age rclone curl tzdata
COPY scripts/backup.sh /usr/local/bin/backup
RUN chmod 755 /usr/local/bin/backup
USER 65534:65534
ENTRYPOINT ["/bin/sh","-c","while true; do /usr/local/bin/backup || true; sleep \"${BACKUP_INTERVAL_SECONDS:-3600}\"; done"]
