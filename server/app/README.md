# kan! server (MVP)

A small, self-hostable server that the **unmodified osu!lazer client** (the kan!
fork) can talk to: accounts, level browsing/downloading, score submission and
per-level leaderboards. It implements the wire protocol the client already
speaks — clean-room from the client's MIT source, see
[`../docs/lazer-protocol.md`](../docs/lazer-protocol.md) — and the MVP scope in
[`../docs/mvp-scope.md`](../docs/mvp-scope.md).

One NestJS process (Node 22, TypeScript) + PostgreSQL 16. No Redis, no workers,
no search service: the whole stack is budgeted at **≤ 700 MB RAM** so it fits the
project's 1 GB ARM64 host next to the Nebula lighthouse
([`../docs/hosting-study.md`](../docs/hosting-study.md)).

## Endpoints

| Route | Purpose |
|---|---|
| `POST /oauth/token` | OAuth2 password + refresh_token grants |
| `POST /users` | registration (client's `form_error` shape) |
| `GET /api/v2/me`, `/api/v2/me/`, `/api/v2/me/{mode}` | current user |
| `GET /api/v2/users/{idOrName}[/{mode}]?key=` | profile |
| `GET /api/v2/users/{id}/scores/{best\|recent}` | profile score sections |
| `GET /api/v2/beatmapsets/search` | listing overlay search, `cursor[key]` paging |
| `GET /api/v2/beatmapsets/{id}`, `/api/v2/beatmapsets/lookup?beatmap_id=` | set details |
| `GET /api/v2/beatmaps/lookup?id\|checksum\|filename` | beatmap lookup (checksum = `.osu` MD5) |
| `GET /api/v2/beatmaps/?ids[]=` | batch lookup |
| `GET /api/v2/beatmapsets/{id}/download` | the `.osz` (302 to presigned URL with the s3 driver) |
| `POST /api/v2/beatmaps/{id}/solo/scores` | score token |
| `PUT /api/v2/beatmaps/{id}/solo/scores/{token}` | score submission |
| `GET /api/v2/beatmaps/{id}/scores` | leaderboard (`type`, `mode=kan`, `mods[]`, `limit`) |
| `GET /api/v2/friends`, `/api/v2/blocks`, `/api/v2/me/beatmapset-favourites` | empty collections |

## Run it locally

```bash
cd server/app
npm install
docker compose up -d              # PostgreSQL 16 on host port 55432 (mem_limit 512m)
cp .env.example .env              # adjust if needed (no real secrets in git)
npm run migrate                   # apply migrations/*.sql
npm run import -- /path/to/level.osz --owner <username>   # optional
npm run start:dev                 # http://localhost:3000
```

The dev database is `postgres://kan:kan@localhost:55432/kan` (see `.env.example`).
Stop the database with `docker compose down` (the named volume with the data is
kept).

### Tests, lint, build

```bash
npm test        # unit tests + e2e against the docker postgres (creates a throwaway kan_test DB)
npm run lint
npm run build
npm run typecheck
```

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `PORT` | `3000` | HTTP port |
| `DATABASE_URL` | `postgres://kan:kan@localhost:55432/kan` | PostgreSQL connection string |
| `OAUTH_CLIENT_ID` | `kan-client` | client id the game sends |
| `OAUTH_CLIENT_SECRET` | `kan-public-client` | client "secret" the game sends — **public by design**, see below |
| `ACCESS_TOKEN_TTL_SECONDS` | `86400` | access token lifetime (keep ≫ 30 s) |
| `REFRESH_TOKEN_TTL_SECONDS` | `2592000` | refresh token lifetime (rotated on use) |
| `SCORE_TOKEN_TTL_SECONDS` | `1800` | score token lifetime (one play) |
| `STORAGE_DRIVER` | `local` | `local` or `s3` |
| `STORAGE_LOCAL_DIR` | `./data` | root directory for the local driver |
| `S3_ENDPOINT`, `S3_REGION`, `S3_BUCKET`, `S3_ACCESS_KEY_ID`, `S3_SECRET_ACCESS_KEY`, `S3_FORCE_PATH_STYLE` | — | S3-compatible store (Cloudflare R2 / AWS S3) |
| `DOWNLOAD_URL_EXPIRY_SECONDS` | `300` | expiry of the presigned download URLs |

## Client configuration

The fork's `KanEndpointConfiguration` ships the OAuth client as constants and
picks the server address from the environment:

```csharp
CLIENT_ID     = "kan-client";        // OAUTH_CLIENT_ID
CLIENT_SECRET = "kan-public-client"; // OAUTH_CLIENT_SECRET
ServerUrl     = KAN_SERVER_URL env var (testing / community servers)
                ?? built-in default URL (empty until kan! has a public server)
```

So the server-side values in `.env` are `OAUTH_CLIENT_ID=kan-client` and
`OAUTH_CLIENT_SECRET=kan-public-client`. **The client "secret" is not a
secret**: it ships inside every copy of the game (like osu!lazer's public
client), and the real protection is user passwords (argon2id) plus opaque,
hashed bearer and refresh tokens. Keep the values in sync with the client build.

Two small client-side changes are needed later for media (documented in
[`../docs/mvp-scope.md`](../docs/mvp-scope.md) §8): widen the trusted texture
hosts beyond `*.ppy.sh` and point preview audio at the server. Covers and
previews are returned empty until then — the client tolerates that.

## Level import

```bash
npm run import -- <file.osz> [--owner <username>]
```

Unzips the archive, parses every `.osu`, rewrites `BeatmapID:`/`BeatmapSetID:`,
re-zips and stores the result (local directory or the S3 bucket), computes each
rewritten `.osu`'s MD5 as the beatmap checksum (that is what the client looks up
and later sends as `beatmap_hash`), and extracts the cover image. Re-importing an
archive whose set id is already present updates the set instead of duplicating
it. Imported sets are `ranked` for now, so leaderboards show immediately.

Only mode-0 (kan!-playable) difficulties are imported — kan! claims legacy
ruleset id 0, so taiko/catch/mania difficulties cannot be played. They are
skipped with a log line and stripped from the stored archive; an archive with no
mode-0 difficulty is rejected (nothing is written, exit code 1).

## Production

See [`deploy/DEPLOY.md`](deploy/DEPLOY.md): one small ARM64 host, Caddy for TLS,
`docker-compose.prod.yml` with `mem_limit`s summing to 680 MB, nightly `pg_dump`
backups to object storage.

## Layout

```
src/            NestJS modules: auth, users, beatmaps, scores, storage, db, api
migrations/     plain SQL, applied in order by src/db/migrate.ts
test/           unit tests + client-flow e2e (jest + supertest)
deploy/         production compose, Caddyfile, Dockerfile, backup script, DEPLOY.md
```
