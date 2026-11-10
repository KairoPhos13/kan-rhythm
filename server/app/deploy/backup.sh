#!/bin/sh
# kan! server: nightly PostgreSQL backup, kept locally and uploaded to R2.
#
# Usage (as root, from kan-backup.service):
#   BACKUP_DIR=/var/backups/kan BACKUP_BUCKET=kan-backups /usr/local/sbin/kan-backup.sh
#
# The R2 keys are read from deploy/.env at run time (S3_ENDPOINT,
# S3_ACCESS_KEY_ID, S3_SECRET_ACCESS_KEY) and handed to rclone through its
# environment: no rclone config file with secrets is ever written.
set -eu

DEPLOY_DIR=${DEPLOY_DIR:-$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)}
COMPOSE_FILE=${COMPOSE_FILE:-docker-compose.tunnel.yml}
BACKUP_DIR=${BACKUP_DIR:-/var/backups/kan}
BACKUP_BUCKET=${BACKUP_BUCKET:-}
LOCAL_DAYS=${LOCAL_DAYS:-14}
REMOTE_DAYS=${REMOTE_DAYS:-60}
STAMP=$(date -u +%Y%m%d-%H%M%S)
OUT="$BACKUP_DIR/kan-$STAMP.sql.gz"

umask 077
mkdir -p "$BACKUP_DIR"

# Written to a temporary name first: a failed dump never looks like a backup.
docker compose -f "$DEPLOY_DIR/$COMPOSE_FILE" exec -T postgres \
	pg_dump -U kan -d kan --no-owner | gzip > "$OUT.part"
gzip -t "$OUT.part"
mv "$OUT.part" "$OUT"
echo "backup written to $OUT ($(du -h "$OUT" | cut -f1))"

find "$BACKUP_DIR" -name 'kan-*.sql.gz' -mtime +"$LOCAL_DAYS" -delete

if [ -n "$BACKUP_BUCKET" ]; then
	env_get() { sed -n "s/^$1=//p" "$DEPLOY_DIR/.env" | tail -n 1; }
	export RCLONE_CONFIG_R2_TYPE=s3
	export RCLONE_CONFIG_R2_PROVIDER=Cloudflare
	RCLONE_CONFIG_R2_ENDPOINT=$(env_get S3_ENDPOINT)
	RCLONE_CONFIG_R2_ACCESS_KEY_ID=$(env_get S3_ACCESS_KEY_ID)
	RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=$(env_get S3_SECRET_ACCESS_KEY)
	export RCLONE_CONFIG_R2_ENDPOINT RCLONE_CONFIG_R2_ACCESS_KEY_ID RCLONE_CONFIG_R2_SECRET_ACCESS_KEY
	export RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
	rclone copy "$OUT" "r2:$BACKUP_BUCKET/" --s3-no-check-bucket
	rclone delete "r2:$BACKUP_BUCKET/" --min-age "${REMOTE_DAYS}d" --include 'kan-*.sql.gz'
	echo "uploaded to r2:$BACKUP_BUCKET/ (dumps older than $REMOTE_DAYS days removed there)"
fi
