# kan! server MVP (lazer protocol): scope

**Status: Draft for owner review.**

Protocol facts used throughout: [`lazer-protocol.md`](lazer-protocol.md) (clean-room from the
client's MIT source). Distribution decision: [`osu-server-feasibility.md`](osu-server-feasibility.md)
§7-8, recommendation **B** (small own server, lazer protocol). Hosting: [`hosting-study.md`](hosting-study.md).
The target host is the owner's AWS **t4g.micro** (ARM64 Graviton, 1 GB RAM, 4 GB free disk) which
already runs a Nebula lighthouse (~10-50 MB RSS).

## 1. Budget and shape

One NestJS process (Node 22, TypeScript) + PostgreSQL 16 behind a proxy. No Redis, no worker
processes, no Elasticsearch (search is SQL `ILIKE` at a few hundred levels). Everything must run on
**arm64** (`argon2` and `pg` are fine; no x86-only native modules).

The deployed stack is the **Cloudflare Tunnel** one (`app/deploy/docker-compose.tunnel.yml`:
app + postgres + cloudflared, level files in Cloudflare R2, no published ports); the direct
alternative with Caddy for TLS is `app/deploy/docker-compose.prod.yml` + `Caddyfile`. Both and
the reasons are documented in `app/deploy/DEPLOY.md`.

Memory budget for the whole stack: **≤ 700 MB** (`mem_limit`s: tunnel
app 300 MB + postgres 320 MB + cloudflared 64 MB = 684 MB; Caddy alternative
app 300 MB + postgres 320 MB + caddy 60 MB = 680 MB), PostgreSQL tuned small
(`shared_buffers=64MB`, `max_connections=20`, `work_mem=4MB`). 1-2 GB swap file on the host is
expected (see `app/deploy/DEPLOY.md`). Fits next to the Nebula lighthouse per
`hosting-study.md` §2 "tight" row + reviewer note.

## 2. Endpoints

In (all shapes per `lazer-protocol.md`, section refs in parentheses):

| Route | Notes |
|---|---|
| `POST /oauth/token` | password + refresh_token grants (§1) |
| `POST /users` | registration, `form_error` shape (§2) |
| `GET /api/v2/me`, `/me/`, `/me/{mode}` | (§3.1); `session_verification_method` omitted |
| `GET /api/v2/users/{lookup}/{mode}?key=` | profile (§3.2) |
| `GET /api/v2/users/{id}/scores/{best\|recent}` | profile sections (§3.4) |
| `GET /api/v2/beatmapsets/search` | paging via `cursor[key]` params (§4.1) |
| `GET /api/v2/beatmapsets/{id}`, `/beatmapsets/lookup?beatmap_id=` | (§4.2) |
| `GET /api/v2/beatmaps/lookup?id\|checksum\|filename` | checksum = MD5 of the rewritten `.osu` (§4.3) |
| `GET /api/v2/beatmaps/?ids[]=` | batch lookup (§4.4) |
| `GET /api/v2/beatmapsets/{id}/download` | `.osz`; s3 driver = **302 to presigned URL** (5 min), local driver streams (§4.7) |
| `POST /api/v2/beatmaps/{id}/solo/scores` | score token (§5.1) |
| `PUT /api/v2/beatmaps/{id}/solo/scores/{token}` | score submit (§5.2) |
| `GET /api/v2/beatmaps/{id}/scores` | leaderboard, `mode=kan`, best per user, top 50 + `user_score` (§5.4) |
| `GET /api/v2/friends`, `/blocks`, `/me/beatmapset-favourites` | empty collections (§9), no retry noise |

Out of MVP (client fails silently or never calls): chat, notifications, websocket/SignalR hubs,
seasonal backgrounds, replay download, 2FA (`session_verification_method` stays null), website
pages, editor upload. Covers and previews return **null/empty** (§8.3).

## 3. Data model (PostgreSQL, plain SQL migrations in `app/migrations/`)

| Table | Key columns |
|---|---|
| `users` | `id`, `username` (unique, case-insensitive), `email`, `password_hash` (argon2id), `country_code`, `join_date`, `avatar_url`/`cover_url` (empty) |
| `oauth_tokens` | `id`, `user_id`, `access_token_hash`, `refresh_token_hash` (SHA-256 of opaque random tokens), `access_expires_at`, `refresh_expires_at` |
| `beatmapsets` | `id` (own sequence), `user_id` (owner), `title*/artist*/creator/source/tags`, `status` (smallint, `1` = ranked for now), `bpm`, `submitted_date`, `ranked_date`, `last_updated`, `genre_id`/`language_id`, `archive_key`, `cover_key` |
| `beatmaps` | `id` (own sequence), `beatmapset_id`, `version`, `checksum` (**MD5 of the rewritten `.osu`**, unique), `filename`, `mode_int`, `cs/ar/accuracy/drain`, `total_length`, `hit_length`, `max_combo`, `bpm`, `last_updated` |
| `scores` | `id`, `user_id`, `beatmap_id`, `ruleset_id` (0) + `ruleset_short_name` ("kan") stored as data, `total_score`, `accuracy`, `max_combo`, `rank`, `passed`, `mods`, `statistics`, `maximum_statistics`, `started_at`, `ended_at`, `build_id` |
| `score_tokens` | `id`, `user_id`, `beatmap_id`, `beatmap_hash`, `ruleset_id`, `version_hash`, `expires_at`, `used` |

Leaderboard = best score per `(user, beatmap, ruleset)` ordered by `total_score` desc then `ended_at`
asc, top 50, plus the requesting user's own best and position.

## 4. Level import flow (`npm run import -- <file.osz> [--owner <username>]`)

Unzip → parse every `.osu` (`[Metadata]`, `[General]`, `[Difficulty]`, `[TimingPoints]`,
`[HitObjects]`, `[Events]` background) → allocate set/difficulty ids → rewrite `BeatmapID:` /
`BeatmapSetID:` into each `.osu` → re-zip → **MD5 each rewritten `.osu`** (that is the checksum the
client looks up and later sends as `beatmap_hash`) → store the archive and the cover image through
the `Storage` interface. Re-importing an archive whose `BeatmapSetID` already exists updates that set
(new version) instead of duplicating. Everything imports as status `ranked` (leaderboards show);
the column stays for a later review workflow.

## 5. Auth / token design

OAuth2-ish as the client speaks it (`lazer-protocol.md` §1): `POST /oauth/token` with
`grant_type=password|refresh_token`, `client_id`/`client_secret` checked against env config,
`scope=*`. Opaque random tokens (32 bytes, base64url) stored **hashed** (SHA-256); access token
24 h, refresh token 30 d and **rotated on use**. Bearer auth on every `/api/v2` route that needs it;
401 only for genuinely invalid tokens (a stray 401 logs the client out, §0). Password hashing:
argon2id. Rate limiting (in-memory, per IP) on `/oauth/token` and `/users`; request size limits;
error bodies `{"error": "..."}` (registration: `form_error` shape); no stack traces; no CORS; logs
never contain passwords or tokens.

## 6. Storage

`Storage` interface with two drivers: `local` (dev/tests, files under `STORAGE_LOCAL_DIR`) and `s3`
(S3-compatible: Cloudflare R2 / AWS S3 via `@aws-sdk/client-s3` +
`@aws-sdk/s3-request-presigner`). Keys: `beatmapsets/{id}.osz`, `beatmapsets/{id}/cover.jpg`.

## 7. File layout / how to run

`server/app/`: NestJS sources (`src/`), plain SQL (`migrations/`), tests (`test/`), dev compose
(`docker-compose.yml`, postgres:16-alpine on host port **55432**), `.env.example`, `README.md`,
`deploy/` (`docker-compose.tunnel.yml`, `docker-compose.prod.yml`, `Caddyfile`, `Dockerfile`,
`DEPLOY.md`). Local loop:
`docker compose up -d` → `npm run migrate` → `npm run import -- <file.osz>` → `npm run start:dev`;
tests: `npm test` (unit + e2e against the dev postgres), `npm run lint`, `npm run build`.

## 8. Client changes needed in the fork (Claude will make these; **not** done by the server work)

1. **Trusted media hosts:** `game/client/osu.Game/Online/TrustedDomainOnlineStore.cs:12-21` only
   loads online textures from `*.ppy.sh`, so covers/avatars from our domain silently do not render.
   Widen the allow-list to include the configured API/website host.
2. **Preview URL host:** preview audio is hardcoded to `https://b.ppy.sh/preview/{setid}.mp3`
   (`game/client/osu.Game/Audio/PreviewTrackManager.cs:114`). Point it at the server (or leave it
   broken; previews are out of MVP scope and may stay empty).

## 9. Risks

- **Protocol drift** vs client updates: `lazer-protocol.md` stays the checked contract; e2e tests
  walk the client's exact JSON shapes.
- **1 GB RAM is tight** (`hosting-study.md` reviewer note): `mem_limit`s + swap; re-measure after
  first deploy. The import CLI runs as a one-shot container/command, not as a service.
- **No anti-cheat** in MVP: a score is the client's own claim. Only well-formed values are
  stored (ranges validated and clamped in `src/scores/score-rules.ts`, backed by CHECK
  constraints) and ties break on the server's own clock, but nothing checks that a score was
  really played (matches "simple beats clever" scale).
- **`ruleset_id 0` collision** with osu!standard semantics: stored as data
  (`ruleset_short_name='kan'`, `ruleset_id=0`) so nothing assumes "0 = osu!standard".
- **No pp / difficulty calculation**: star ratings are 0 and leaderboards rank by total score;
  revisiting this later means an ADR-0008 `kp` pass, not a schema change.
- **Covers/previews empty** until the client changes of §8 land (expected, documented in
  `lazer-protocol.md` §8.3).
