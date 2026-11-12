# Deploying the kan! server MVP

**Recommended path: Cloudflare Tunnel.** The host is the project's AWS
**t4g.micro** (ARM64 Graviton, 1 GB RAM, 4 GB free disk) whose public IP is also
the Nebula lighthouse and must stay hidden. With Cloudflare Tunnel there are
**no inbound ports at all**: `cloudflared` makes an outbound connection to
Cloudflare's edge, TLS terminates there, and level files live in **R2**. The
whole stack is budgeted at **≤ 700 MB RAM** (app 300 MB + postgres 320 MB +
cloudflared 64 MB = 684 MB, enforced by `mem_limit`s) and runs next to the
Nebula lighthouse (~10-50 MB RSS).

Everything here is *documentation only*: nothing is deployed from this
repository and no credentials live in it.

Contents:

1. [Cloudflare account and domain](#1-cloudflare-account-and-domain)
2. [R2 bucket for level files and backups](#2-r2-bucket-for-level-files-and-backups)
3. [Create the tunnel](#3-create-the-tunnel)
4. [Prepare the AWS box](#4-prepare-the-aws-box)
5. [Configure deploy/.env](#5-configure-deployenv)
6. [Start the stack](#6-start-the-stack)
7. [Security group](#7-security-group)
8. [Check it works](#8-check-it-works)
9. [Import levels](#9-import-levels)
10. [Players: password reset and accounts](#10-players-password-reset-and-accounts)
11. [Backups (pg_dump to R2) and a restore drill](#11-backups-pg_dump-to-r2-and-a-restore-drill)
12. [What to put in the game](#12-what-to-put-in-the-game)
13. [Update / restore](#13-update--restore)
14. [Memory watch and troubleshooting](#14-memory-watch-and-troubleshooting)
15. [Alternative: direct exposure with Caddy](#15-alternative-direct-exposure-with-caddy)

---

## 1. Cloudflare account and domain

1. Create a Cloudflare account with the project mailbox (the `KairoPhos`
   address, never a personal one) and turn on two-factor authentication.
2. In **Cloudflare Registrar**, buy the domain (e.g. `kan.example.org`'s parent
   `example.org`). WHOIS redaction is **on by default**; leave it on. Nothing
   in the public registration data should name a person.
3. Let Cloudflare manage DNS for the domain (the dashboard walks through it).

## 2. R2 bucket for level files and backups

1. In the Cloudflare dashboard open **R2 → Create bucket**, e.g. `kan-levels`
   (level archives) and `kan-backups` (database dumps). Region: automatic is
   fine; R2 egress is free (see `server/docs/hosting-study.md` §4).
2. **Manage R2 API Tokens → Create API token**: scope it to *these two buckets*
   only (Object Read & Write). Copy the **Access Key ID** and **Secret Access
   Key** now; they go into `deploy/.env` only (step 5), never into git, chat or
   a ticket. If the token leaks, revoke it and create a new one.

## 3. Create the tunnel

1. Dashboard → **Zero Trust → Networks → Tunnels → Create a tunnel** →
   choose **Cloudflared** → name it `kan-server`.
2. Pick the **Docker** environment and copy the token from the shown command
   (it is the long string after `--token`). That is `TUNNEL_TOKEN` for step 5.
3. In the tunnel's **Public Hostname** tab add one route:
   - Hostname: `api.example.org` (a subdomain; the apex works too)
   - Service: **HTTP** → `app:3000`
4. The tunnel runs from a container on the compose network, so `app:3000` is
   exactly right. No other hostname is needed.

## 4. Prepare the AWS box

SSH in (`ssh ubuntu@<host>`, the user depends on the AMI) and give the 1 GB
host a swap file (turns memory spikes into EBS latency instead of OOM kills):

```bash
sudo fallocate -l 2G /swapfile        # or 1G if disk is tight
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
sudo sysctl vm.swappiness=10
echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-kan-swap.conf
free -h                               # Swap should show 2.0Gi
```

Install Docker (Ubuntu 22.04/24.04 LTS arm64):

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2 rclone
sudo usermod -aG docker "$USER"       # log out and back in afterwards
docker version && docker compose version
```

Amazon Linux 2023: `sudo dnf install -y docker docker-compose-plugin rclone`
then `sudo systemctl enable --now docker` and the same `usermod`.

Get the code onto the host (clone the repository or copy the `server/app/`
folder) and remember where it lives, e.g. `~/kan-rhythm/server/app`.

## 5. Configure deploy/.env

Create `server/app/deploy/.env`; it is gitignored and must never be committed:

```bash
cd server/app/deploy
cp .env.example .env
```

Fill it in (generate the database password with `openssl rand -hex 32`):

```dotenv
POSTGRES_PASSWORD=<long-random>
TUNNEL_TOKEN=<the token from the Cloudflare tunnel page>
TRUST_PROXY=cloudflare
STORAGE_DRIVER=s3
S3_ENDPOINT=https://<account-id>.r2.cloudflarestorage.com
S3_REGION=auto
S3_BUCKET=kan-levels
S3_ACCESS_KEY_ID=<R2 API token access key id>
S3_SECRET_ACCESS_KEY=<R2 API token secret>
OAUTH_CLIENT_ID=kan-client
OAUTH_CLIENT_SECRET=kan-public-client
# below Cloudflare's 100 MB edge limit (see the note under this block)
SUBMISSION_MAX_PACKAGE_BYTES=94371840
```

Two notes:

- **`OAUTH_CLIENT_SECRET` is not a secret**: the game ships `kan-client` /
  `kan-public-client` as constants (like osu!lazer's public client). Real
  protection is user passwords (argon2id) and hashed bearer/refresh tokens.
- **`TRUST_PROXY=cloudflare`** makes the app trust Cloudflare's `CF-Connecting-IP`
  for rate limiting and use `https` in generated URLs. Only ever set it when a
  proxy we control (the tunnel) fronts the app; leave it empty otherwise. (The
  value `caddy` exists for the direct Caddy stack of §15, where the forwarded
  address is the one Caddy appends itself.)

One more setting matters through Cloudflare: the edge rejects request bodies
over **100 MB** (Free/Pro plans; adjustable in the zone's **Network** page)
before they reach the app. The default `SUBMISSION_MAX_PACKAGE_BYTES` is 100
MiB, which is over that line once the multipart framing is counted, so set it
below the edge limit for level uploads to work, e.g. `SUBMISSION_MAX_PACKAGE_BYTES=94371840`
(90 MiB), or raise the zone's Maximum Upload Size. The app enforces its own
limit too: the upload streams to a temporary file with a running byte counter,
and the archive is checked (entry count, declared sizes, ratio) before it is
inflated.

## 6. Start the stack

```bash
cd server/app/deploy
docker compose -f docker-compose.tunnel.yml up -d --build
docker compose -f docker-compose.tunnel.yml ps      # app, postgres, cloudflared running
```

Migrations run automatically when the app container starts (they take a
PostgreSQL advisory lock, so several instances starting together queue up
instead of applying one migration twice); to run them by
hand: `docker compose -f docker-compose.tunnel.yml exec app node dist/db/migrate-cli.js`.
Logs (no passwords or tokens are logged):
`docker compose -f docker-compose.tunnel.yml logs -f app`.

## 7. Security group

On the AWS instance's security group: **close inbound 80 and 443**; with
Cloudflare Tunnel nothing needs to listen publicly (the tunnel dials out).
**Keep the Nebula UDP port and SSH exactly as they are.**

### 7b. Keep the containers away from the host and Nebula

The compose stack is hardened (no capabilities, no privilege gain, read-only
images, process limits; postgres on an internal network with no way out).
Docker would still let a container reach anything the host can route to: the
Nebula overlay, the VPC, the EC2 metadata service and the host's own ssh.
`egress-guard.sh` drops all of that (only DNS to the VPC resolver stays open)
and runs at every boot:

```bash
sudo install -m 755 deploy/egress-guard.sh /usr/local/sbin/kan-egress-guard.sh
sudo install -m 644 deploy/kan-egress-guard.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now kan-egress-guard
sudo iptables -S DOCKER-USER    # the drops for 169.254/16, Nebula, the VPC, RFC 1918
```

Set `NEBULA_NET`, `VPC_NET` and `VPC_DNS` in the unit's environment if the
host's networks differ. Also in the EC2 console: **IMDSv2 required, hop limit
1**, and no IAM role unless the host needs one.

Security updates install by themselves (`dnf-automatic`, `upgrade_type =
security`, `apply_updates = yes`); kernel updates need a reboot
(`sudo dnf upgrade --releasever=latest -y && sudo reboot` now and then).

## 8. Check it works

From your own machine (not the box):

```bash
curl -s https://api.example.org/
# {"name":"kan-server","api":"https://api.example.org/api/v2"}
curl -s https://api.example.org/api/v2/users/1?key=id
# {"error":"User not found"}   (or a user JSON once someone registered)
curl -s -X POST https://api.example.org/users -H 'Content-Type: application/x-www-form-urlencoded' \
  --data-urlencode 'user[username]=smoketest' --data-urlencode 'user[user_email]=smoke@example.org' \
  --data-urlencode 'user[password]=a good password'
# {"id":1,"username":"smoketest"}
```

On the box: `docker stats --no-stream` (app ~120-250 MB, postgres ~150-300 MB,
cloudflared ~30-60 MB) and `free -h`. In the Cloudflare dashboard the tunnel
should show as **Healthy**.

## 9. Import levels

Level archives are imported from the CLI (one-shot; nothing listens for
uploads). Copy the `.osz` onto the host and run the importer inside the app
container:

```bash
cd server/app/deploy
docker compose -f docker-compose.tunnel.yml cp ./some-level.osz app:/tmp/some-level.osz
docker compose -f docker-compose.tunnel.yml exec app \
  node dist/import/cli.js /tmp/some-level.osz --owner <username>
```

- `--owner <username>` must be a registered player; without it the first
  registered user owns the set.
- Only mode-0 (kan!-playable) difficulties are imported; the others are skipped
  and stripped from the stored archive.
- Re-importing an archive that carries one of our `BeatmapSetID`s updates that
  set instead of duplicating it. Imported sets are `ranked`, so leaderboards
  show immediately.

## 10. Players and levels: the admin CLI

kan! sends no email, so a player who forgets their password asks the owner (the
website's "Forgot your password?" note points at GitHub issues). The admin CLI
in the app container handles that and the small account and catalogue tasks
around it. It prints account names, level titles and dates only: never email
addresses, password hashes or tokens.

```bash
cd server/app/deploy
# list accounts: id, username, join date, last visit
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js users
# set a new random password for one player
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js reset-password <username>
# delete an account that owns no beatmap sets (--yes is required)
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js delete-user <username> --yes
# list levels: id, status, creator, title
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js levels
# move a level to rank, pending, wip, graveyard or loved
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js set-status <id> <status>
# delete a level with its package and cover (--yes is required)
docker compose -f docker-compose.tunnel.yml exec app node dist/admin/cli.js delete-level <id> --yes
```

Quote usernames that contain spaces, e.g. `reset-password "some player"`.

- `reset-password` prints a new random password once (20 characters from a
  readable alphabet) and deletes the player's OAuth tokens, so every signed-in
  client must sign in again. kan! has no change-password screen, so **give the
  new password to the player privately** (a direct message, never the public
  issue) and tell them to keep it safe. Running the command again is how the
  password is changed later.
- `delete-user` removes the account with everything that references it: scores,
  score tokens and OAuth tokens, in one transaction. It **refuses** while the
  player owns beatmap sets: the schema could leave those sets without an owner
  (`ON DELETE SET NULL`) but ownerless sets have no place in the catalog. Move
  them to another account first (`UPDATE beatmapsets SET user_id = ...` in
  psql) or delete them with `delete-level`, then run the command again.
- **Deleting an account completely** is therefore two steps, in this order:
  `delete-level <id> --yes` for every level the player owns (that also removes
  the stored package and cover from storage), then `delete-user <username>
  --yes`. `delete-user` alone leaves uploaded levels and their stored files
  where they are, so deleting the levels first is what keeps storage in step
  with the database. Both steps delete data for good; take a backup first if
  anything might be wanted later.
- `levels` lists every level in the catalogue (id, status, creator, title).
- `set-status` is how levels get ranked: players submit their levels as pending
  or WIP from the game (see `../README.md`, "Level submission from the game")
  and the owner moves them on. `rank` and `loved` set the ranked date, and
  `pending`, `wip` and `graveyard` clear it again. A ranked level keeps its
  status when its creator resubmits it; pending/WIP/graveyard levels follow the
  creator's choice.
- `delete-level` removes the level with everything that belongs to it:
  difficulties, scores and score tokens in the database, and the stored package
  and cover in storage (the local directory or the R2 bucket).
- The exit code is 0 on success and 1 on any problem (unknown user or level,
  refused deletion, bad arguments); usage text is printed when the arguments do
  not parse.

## 11. Backups (pg_dump to R2) and a restore drill

Every night a systemd timer dumps the database, keeps 14 days on the host
(`/var/backups/kan`) and uploads the dump to the `kan-backups` bucket, where
dumps older than 60 days are removed. The R2 keys come from `deploy/.env` at
run time; no rclone config file with secrets is written. (Amazon Linux 2023
has no cron, hence the timer.)

```bash
sudo install -m 755 deploy/backup.sh /usr/local/sbin/kan-backup.sh
sudo install -m 644 deploy/kan-backup.service deploy/kan-backup.timer /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now kan-backup.timer
sudo systemctl start kan-backup.service          # one backup now
journalctl -u kan-backup.service -n 5 --no-pager # "uploaded to r2:kan-backups/"
systemctl list-timers kan-backup.timer           # the next run
```

`kan-backup.service` sets `DEPLOY_DIR` (the folder with `.env` and the compose
file), `BACKUP_DIR` and `BACKUP_BUCKET`; the R2 token must be allowed to write
to `kan-backups`.

**Restore drill** (done 2026-11-10: the newest dump from R2 restored into a
throwaway database with every table and account; repeat twice a year):

```bash
cd ~/kan-server/deploy
env_get() { sed -n "s/^$1=//p" .env | tail -n 1; }
export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare \
  RCLONE_CONFIG_R2_ENDPOINT=$(env_get S3_ENDPOINT) \
  RCLONE_CONFIG_R2_ACCESS_KEY_ID=$(env_get S3_ACCESS_KEY_ID) \
  RCLONE_CONFIG_R2_SECRET_ACCESS_KEY=$(env_get S3_SECRET_ACCESS_KEY)
F=$(rclone lsf r2:kan-backups/ | sort | tail -1); rclone copy r2:kan-backups/$F /tmp/
P="sudo docker exec -i kan-server-postgres psql -U kan -q"
$P -d kan -c "CREATE DATABASE kan_drill;"
gunzip -c /tmp/$F | $P -d kan_drill -v ON_ERROR_STOP=1 >/dev/null
$P -d kan_drill -tAc "SELECT count(*) FROM users;"
$P -d kan -c "DROP DATABASE kan_drill;"
```

A real restore replaces `kan_drill` with `kan` after stopping the app
(`docker compose -f docker-compose.tunnel.yml stop app`) and recreating the
database.

With `STORAGE_DRIVER=s3` the level archives themselves live in R2 and need no
extra backup; with `STORAGE_DRIVER=local` also back up the `kan-storage` volume
(`docker run --rm -v kan-storage:/data -v /var/backups/kan:/backup alpine tar czf /backup/kan-storage.tar.gz -C /data .`).

## 12. What to put in the game

The client (`game/client/osu.Game/Online/KanEndpointConfiguration.cs`) picks
its server like this:

```csharp
CLIENT_ID     = "kan-client";         // matches OAUTH_CLIENT_ID
CLIENT_SECRET = "kan-public-client";  // matches OAUTH_CLIENT_SECRET
ServerUrl     = KAN_SERVER_URL env var (testing / community servers)
                ?? built-in default URL
```

So nothing is configured in the game for players: **the built-in default URL
is the deployed server** (the client's `default_server_url`, today
`https://kan-api.kairophos.link`). For testing against this deployment set the environment
variable `KAN_SERVER_URL=https://api.example.org` before starting the game.

## 13. Update / restore

```bash
cd server/app && git pull        # or copy the new tree
cd deploy
docker compose -f docker-compose.tunnel.yml up -d --build
```

## 14. Memory watch and troubleshooting

```bash
docker stats --no-stream      # limits: app 300m, postgres 320m, cloudflared 64m
free -h                       # some swap in use under load is expected
```

- **Tunnel shows "Down"**: `docker compose -f docker-compose.tunnel.yml logs
  cloudflared`; the token must match the tunnel in the dashboard.
- **`curl https://api.example.org/` fails with 502/530**: app not running or
  the public hostname points at something other than `http://app:3000`.
- **The game client logs out after one request**: a stray HTTP 401 kills the
  session (see `server/docs/lazer-protocol.md` §0); check the app log for the
  failing route.
- **Downloads fail with a signed-URL error**: `S3_*` values wrong or the token
  cannot read the bucket; check with `rclone lsf remote:kan-levels/`.
- **Host OOMs**: grow the swap file to 2 GB and lower `mem_limit` of `app`
  first; keep postgres at `shared_buffers=64MB`. Re-measure real numbers after
  the first week and update the limits if there is headroom.

## 15. Alternative: direct exposure with Caddy

`docker-compose.prod.yml` is the old direct-exposure stack (Caddy for TLS,
ports 80/443 published, `app` + `postgres` + `caddy`, 680 MB). Use it only if
the tunnel is not an option; it **publishes the host's public IP**, which is
the Nebula lighthouse, and needs inbound 80/443 in the security group. Two
caveats if you do:

- Leave `TRUST_PROXY=caddy` (the compose default there): the app then keys its
  rate limits on the client address Caddy forwards, so the per-player limits
  work. `cloudflare` is only right when Cloudflare fronts Caddy as well, and
  then port 443 must be closed to everything but Cloudflare's IP ranges (anyone
  who reaches Caddy directly could otherwise write `CF-Connecting-IP`).
  Leaving it empty keys every client to the Caddy container address, which
  makes the limits global instead of per player, another reason to prefer the
  tunnel.
- The `Caddyfile` allows `SUBMISSION_MAX_PACKAGE_BYTES` (default 100 MiB) plus
  headroom on the level upload routes and 256KB on everything else. Raise the
  `request_body` limit together with `SUBMISSION_MAX_PACKAGE_BYTES` (it needs
  Caddy 2.8 or newer for the matcher form).
- Point the domain's DNS A/AAAA record at the host and replace `kan.example.org`
  in `Caddyfile` before starting.
