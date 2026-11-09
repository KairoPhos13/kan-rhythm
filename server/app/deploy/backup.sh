#!/bin/sh
# kan! server — nightly PostgreSQL backup to a local directory (upload it to
# object storage from there, e.g. with rclone; see DEPLOY.md §7).
#
# Usage: BACKUP_DIR=/var/backups/kan ./backup.sh
set -eu

DEPLOY_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
BACKUP_DIR=${BACKUP_DIR:-/var/backups/kan}
STAMP=$(date -u +%Y%m%d-%H%M%S)
OUT="$BACKUP_DIR/kan-$STAMP.sql.gz"

mkdir -p "$BACKUP_DIR"

docker compose -f "$DEPLOY_DIR/docker-compose.prod.yml" exec -T postgres \
	pg_dump -U kan -d kan | gzip > "$OUT"

echo "backup written to $OUT"

# Keep two weeks of local copies.
find "$BACKUP_DIR" -name 'kan-*.sql.gz' -mtime +14 -delete

# Upload (uncomment and configure rclone once):
#   rclone copy "$OUT" remote:kan-backups/
