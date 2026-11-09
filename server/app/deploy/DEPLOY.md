# Deploying the kan! server MVP

Target host: the project's AWS **t4g.micro** (ARM64 Graviton, 1 GB RAM, 4 GB free
disk), which already runs a Nebula lighthouse (~10-50 MB RSS — it shares the host
fine). Everything here is *documentation only*: nothing is deployed from this
repository and no credentials live in it.

**Memory budget: ≤ 700 MB for the whole stack** (enforced by `mem_limit`s in
`docker-compose.prod.yml`: app 300 MB + postgres 320 MB + caddy 60 MB = 680 MB).
PostgreSQL runs with `shared_buffers=64MB`, `max_connections=20`, `work_mem=4MB`.
There is one Node process, no Redis, no worker.

Contents:

1. [Prerequisites](#1-prerequisites)
2. [Swap file (1-2 GB)](#2-swap-file-1-2-gb)
3. [Install Docker](#3-install-docker)
4. [Configure](#4-configure)
5. [Start the stack](#5-start-the-stack)
6. [Import levels](#6-import-levels)
7. [Backups](#7-backups)
8. [Update / restore](#8-update--restore)
9. [Memory watch](#9-memory-watch)

---

## 1. Prerequisites

- A domain (e.g. from Cloudflare Registrar or Porkbun, privacy redaction on) with
  an A/AAAA record pointing at the host, e.g. `kan.example.org`. Caddy answers
  port 80/443 and gets certificates from Let's Encrypt automatically.
- SSH access to the host (`ssh ubuntu@<host>` — the exact user depends on the AMI).
- Optional but recommended: an S3-compatible bucket for level files (Cloudflare
  R2 free tier is enough — see `server/docs/hosting-study.md` §4) and one for
  backups. With `STORAGE_DRIVER=local`, level files live on the host disk instead
  (fine for a few hundred levels, but they are not backed up by §7).

## 2. Swap file (1-2 GB)

1 GB RAM is tight with swap off; the swap file turns spikes into EBS latency
instead of OOM kills (`server/docs/hosting-study.md` §2).

```bash
sudo fallocate -l 2G /swapfile        # or 1G if disk is tight
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
sudo sysctl vm.swappiness=10
echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-kan-swap.conf
free -h                               # Swap should now show 2.0Gi
```

## 3. Install Docker

Ubuntu 22.04/24.04 LTS (arm64):

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker "$USER"       # log out and back in afterwards
docker version && docker compose version
```

Amazon Linux 2023:

```bash
sudo dnf install -y docker docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker "$USER"
```

## 4. Configure

Get the code onto the host (clone the repository or copy the `server/app/`
folder), then create `server/app/deploy/.env` — it is gitignored and must never
be committed:

```bash
cd server/app/deploy
cp .env.example .env      # if present; otherwise create the file as below
```

`deploy/.env` (generate real random values, e.g. `openssl rand -hex 32`):

```dotenv
POSTGRES_PASSWORD=<long-random>
OAUTH_CLIENT_ID=kan-client
OAUTH_CLIENT_SECRET=<long-random>
# Level files: "s3" (recommended) or "local" (host disk in the kan-storage volume)
STORAGE_DRIVER=s3
S3_ENDPOINT=https://<account>.r2.cloudflarestorage.com   # or https://s3.amazonaws.com
S3_REGION=auto
S3_BUCKET=kan-levels
S3_ACCESS_KEY_ID=<from the storage provider>
S3_SECRET_ACCESS_KEY=<from the storage provider>
S3_FORCE_PATH_STYLE=true
DOWNLOAD_URL_EXPIRY_SECONDS=300
```

Edit `Caddyfile` and replace `kan.example.org` with the real domain.

**Client configuration:** the exact values to put into the client's
`KanEndpointConfiguration` are listed in `server/app/README.md`.

## 5. Start the stack

```bash
cd server/app/deploy
docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml ps      # all three should be "running"/"healthy"
curl -s https://kan.example.org/api/v2/users/1?key=id   # answers JSON (or {"error": ...})
```

Migrations run automatically when the app container starts. To run them by hand:

```bash
docker compose -f docker-compose.prod.yml exec app node dist/db/migrate-cli.js
```

Logs (no passwords or tokens are logged):

```bash
docker compose -f docker-compose.prod.yml logs -f app
```

## 6. Import levels

Level archives are imported from the CLI (one-shot; nothing listens for uploads).
Copy the `.osz` onto the host and run the importer inside the app container:

```bash
cd server/app/deploy
docker compose -f docker-compose.prod.yml cp ./some-level.osz app:/tmp/some-level.osz
docker compose -f docker-compose.prod.yml exec app \
  node dist/import/cli.js /tmp/some-level.osz --owner <username>
```

- `--owner <username>` must be a registered player; without it the first
  registered user owns the set.
- Re-importing an archive that carries one of our `BeatmapSetID`s updates that
  set (same ids, new archive version) instead of duplicating it.
- Every imported set is `ranked` for now, so leaderboards show immediately.

## 7. Backups

Nightly `pg_dump` to a local directory, then upload to object storage
(`deploy/backup.sh` does the dump; wire it to cron and rclone):

```bash
sudo mkdir -p /var/backups/kan
sudo cp server/app/deploy/backup.sh /usr/local/bin/kan-backup.sh
sudo chmod +x /usr/local/bin/kan-backup.sh
echo '17 4 * * * root BACKUP_DIR=/var/backups/kan /usr/local/bin/kan-backup.sh >> /var/log/kan-backup.log 2>&1' \
  | sudo tee /etc/cron.d/kan-backup
```

Upload with rclone (configured once with `rclone config`, target e.g. an R2
bucket `kan-backups`):

```bash
rclone copy /var/backups/kan remote:kan-backups/ --transfers 2
```

If `STORAGE_DRIVER=s3`, level archives live in the bucket and are covered by the
storage provider's durability; with `STORAGE_DRIVER=local` also back up the
`kan-storage` docker volume (`docker run --rm -v kan-storage:/data -v /var/backups/kan:/backup alpine tar czf /backup/kan-storage.tar.gz -C /data .`).

## 8. Update / restore

Update (new code):

```bash
cd server/app && git pull        # or copy the new tree
cd deploy
docker compose -f docker-compose.prod.yml up -d --build
```

Restore the database:

```bash
gunzip -c /var/backups/kan/kan-<stamp>.sql.gz | \
  docker compose -f docker-compose.prod.yml exec -T postgres psql -U kan -d kan
```

## 9. Memory watch

```bash
docker stats --no-stream                 # app ~120-250 MB, postgres ~150-300 MB, caddy ~30 MB
free -h                                  # swap in use is expected under load
```

If the host OOMs: lower `mem_limit` of `app` first, keep postgres at
`shared_buffers=64MB`, and grow the swap file to 2 GB. Re-measure real numbers
after the first week (the `hosting-study.md` figures are estimates) and update
the limits if there is headroom.

Troubleshooting:

- **Caddy cannot get a certificate** — port 80/443 reachable from the internet?
  DNS pointing at the host? `docker compose logs caddy`.
- **The game client logs out after one request** — a stray HTTP 401 kills the
  session (see `server/docs/lazer-protocol.md` §0); check the app log for the
  failing route.
- **Downloads fail with a signed-URL error** — `S3_*` values wrong or the bucket
  is not S3-compatible; try `STORAGE_DRIVER=local` to isolate.
