# Feasibility: adapting the osu! server projects vs. writing a small kan! server

Status: **study for owner decision**, "Draft for owner review". Describes, does not copy: no code,
SQL or text from the studied repositories is reproduced here (osu-web is AGPL-3.0; our repo is MIT).

Method: shallow clones (`git clone --depth 1`) of the ppy repositories under
`server-study/` outside this repo (uncommitted), read on 2026-11-09, plus GitHub metadata for
licences and activity. Paths below are repo-relative (`osu-web/...`, `osu-server-spectator/...`).
Client-side facts come from the kan! client fork (`game/client/`, `game/ruleset/`) and are cited in
`lazer-protocol.md`.

Repositories studied (clone target, licence, size):

| Repository | Licence (evidence) | Code size | Commits in last 12 mo. |
|---|---|---|---|
| `ppy/osu-web` | **AGPL-3.0** (`osu-web/LICENCE` is the AGPL-3 text; `osu-web/composer.json:6`; `osu-web/README.md:40`) | 4,968 PHP files / ~170,351 lines; 318 Blade / ~17,857; 360 TS / ~21,930; 487 Less; 58 CoffeeScript (7,069 tracked files) | **2,507** |
| `ppy/osu-server-spectator` | MIT (`osu-server-spectator/LICENCE`, ppy Pty Ltd) | 219 `.cs` / 31,682 lines | 479 |
| `ppy/osu-queue-score-statistics` | MIT (`osu-queue-score-statistics/LICENCE`) | 121 `.cs` / 16,532 lines | 158 |
| `ppy/osu-elastic-indexer` | MIT (`osu-elastic-indexer/LICENCE`) | 27 `.cs` / 1,381 lines | 0 (last push 2025-04) |
| `ppy/osu-beatmap-difficulty-lookup-cache` | **no licence file** (none in tree, README, csproj; GitHub reports none) | 9 `.cs` / 597 lines | 7 |
| `ppy/osu-server-beatmap-submission` | **no licence file** (same check) | 42 `.cs` / 5,938 lines | 17 |

Also required but not studied in depth (mentioned by the above): `ppy/osu-notification-server`
(websocket push), `ppy/osu-queue-processor` (queue base class), a **beatmap mirror** (a
community-operated mirror project referenced by
`osu-server-beatmap-submission/README.md`), an S3 bucket set, and (for the wiki) the `osu-wiki`
GitHub repo via a GitHub token.

---

## 1. Services and resources

A running osu-web deployment (from `osu-web/compose.yaml`, `osu-web/DEPLOYMENT.md`,
`osu-web/SETUP.md`, `osu-web/config/*.php`, `osu-web/app/Console/Kernel.php`):

| Process | Role | Evidence |
|---|---|---|
| PHP (Laravel 13 + Octane on FrankenPHP, PHP 8.5 ZTS) | web + API | `osu-web/compose.yaml:24-30`, `osu-web/Dockerfile.deployment`, `osu-web/composer.json:88` |
| nginx (assets container) | static assets | `osu-web/DEPLOYMENT.md:34`, `osu-web/docker/deployment/nginx-assets.conf` |
| Node 22 webpack/yarn build | frontend assets (build time) | `osu-web/.node-version`, `osu-web/compose.yaml:40-42` |
| MySQL 8.4 | all core data | `osu-web/compose.yaml:98-99`, `osu-web/SETUP.md:12` |
| Redis | cache + sessions + broadcasting + all queues | `osu-web/config/cache.php:16`, `osu-web/config/session.php:19`, `osu-web/config/broadcasting.php:18` |
| Elasticsearch 7.17 | beatmapset/user/wiki/forum search **and solo-score leaderboards** | `osu-web/compose.yaml:130-132`, `osu-web/SETUP.md:13` |
| `ppy/osu-elastic-indexer` (×2 queues) | scores into ES | `osu-web/compose.yaml:161-176`, `osu-web/app/Libraries/Search/ScoreSearch.php:50` |
| `ppy/osu-queue-score-statistics` | **pp + user stats + medals** (external to the compose file) | `osu-web/config/osu.php:210`, `osu-web/app/Models/Solo/Score.php:551-553` |
| `ppy/osu-notification-server` (×2) | websocket notifications | `osu-web/compose.yaml:69-83` |
| `ppy/osu-beatmap-difficulty-lookup-cache` | on-demand star rating/attributes | `osu-web/compose.yaml:64-67`, `osu-web/app/Libraries/BeatmapDifficultyAttributes.php:14-27` |
| Queue workers | named queues `beatmap_default`, `beatmap_high`, `default`, `notification`, `store-notifications` | `osu-web/DEPLOYMENT.md:53-61` |
| Cron (`artisan schedule:run`, ≤10 min) | 17 scheduled tasks: ranking recalcs, store expiry, token cleanup, daily challenge… | `osu-web/DEPLOYMENT.md:65-69`, `osu-web/app/Console/Kernel.php:27-112` |
| Object storage (S3) or local disks | avatars, previews, replays, screenshots, "central" beatmap files | `osu-web/config/filesystems.php:70-147` |
| Mail (SMTP), optional Camo proxy + image thumbnailer sidecar | | `osu-web/.env.example`, `osu-web/app/Libraries/ImageProcessorService.php` |
| Beatmap mirror(s) | .osz distribution **and** archive processing source (see §7) | `osu-web/app/Models/BeatmapMirror.php`, `osu-web/app/Models/BeatmapsetArchive.php:36-44` |

Official docker setups: `osu-web/compose.yaml` is explicitly a **development** environment
(`osu-web/SETUP.md:1,58-74`; Elasticsearch "uses a high (1+ GB) amount of RAM", `SETUP.md:72`).
`osu-web/Dockerfile.deployment` + `osu-web/DEPLOYMENT.md` build a production image ("the image built
serves multiple purposes", `DEPLOYMENT.md:29`), but **no production stack/orchestration is
provided**; you supply Redis, MySQL, ES, workers, cron, mirrors, S3 yourself.

**We did not run the docker development setup.** Its compose binds host ports 8080/3306/6379/9200/2345/5001
(`osu-web/compose.yaml:66-159`, all below 50000), its own documentation budgets 1+ GB for
Elasticsearch alone (`SETUP.md:72`), and together with MySQL, the Node asset build, PHP Octane and
three sidecar containers it exceeds the 6 GB limit set for this study; it also needs a GitHub token
and Passport keys before it boots usefully (`osu-web/SETUP.md:62`, `osu-web/DEPLOYMENT.md:22-23`).
Skipped as "not easy" per the brief.

Realistic minimum for a few hundred players / a few hundred levels: hardware is not the problem.
One 4 vCPU / 8 GB VM (or a 2 GB web VM + a 2 GB data VM) suffices; the cost is **8-10 distinct
processes from 6+ repositories**, 3 datastores (MySQL, Redis, ES), and a mirror layer. Data volume
is small (low GB in MySQL; archives live on the mirror).

## 2. Feature map for goals 1-4

| Goal | What serves it | Caveats |
|---|---|---|
| (1) register + log in in game | `POST users` → `osu-web/app/Http/Controllers/UsersController.php:189-205`; OAuth via Laravel Passport (`osu-web/composer.json:26-27`, `osu-web/SETUP.md:255-259`), 2FA TOTP (`UserTotpController`) | client registration is gated by config `registration_mode.client`, a User-Agent allow-list and a client-verification token (`ClientCheck::parseToken`); web registration uses Cloudflare Turnstile (`UsersController.php:216`) |
| (2) browse/search/download levels | `BeatmapsetsController@search/lookup/show/download` (`osu-web/routes/web.php:599-603,649`); search via `osu-web/app/Libraries/Search/BeatmapsetSearch*` | search is **ES-only** (no DB fallback); downloads require a configured mirror (§7) |
| (3) score submit + leaderboards | `ScoreTokensController@store`, `Solo\ScoresController@store` (`osu-web/routes/web.php:468,471`); leaderboards `BeatmapsController@soloScores/scores/userScore` (`osu-web/routes/web.php:461-465`) | leaderboards are **ES-only** (`osu-web/app/Libraries/Search/ScoreSearch.php`) and fed by the indexer; best-score queries filter `exists pp` (`ScoreSearch.php:84`), i.e. they require the external pp processor to have run |
| (4) profiles | `UsersController@show/me/scores` (`osu-web/routes/web.php:582-612`), `osu-web/app/Models/User.php`, `UserStatistics/*` | per-mode stats require the per-mode machinery of §3 |

Everything else that comes along: website pages (home, news, changelog, wiki pulled from the
`osu-wiki` GitHub repo, featured artists), forums (8 controllers + BBCode stack), comments, chat
(depends on the notification server), friends/teams/contests/tournaments, rankings (country/team/top
plays/daily challenge, with recalculation crons), multiplayer + playlist rooms + matchmaking,
moderation (6 admin controllers, reports, kudosu, beatmap discussions/nominations), and a full
store with payments (PayPal/Xsolla/Shopify) and supporter tags. Of ~120 controllers only ~20 serve
our goals; roughly half to two-thirds of the app surface is outside them, but the goal-relevant
core is deeply interleaved with it (shared config, models, ES schemas, crons).

## 3. Rulesets: the critical point

**osu-web hard-codes the four osu! modes at every layer.** kan!'s ruleset ids for comparison:
`game/ruleset/KanRuleset.cs:42` declares `KanRuleset : osu.Game.Rulesets.Ruleset, osu.Game.Rulesets.ILegacyRuleset`,
`:53` `LegacyID => 0` (so `RulesetInfo.OnlineID == 0`), `:72` `ShortName => "kan"`.

Where the four modes are fixed in osu-web:

- **PHP constants/enums**: `osu-web/app/Models/Beatmap.php:72-76` (`MODES` map `osu/taiko/fruits/mania → 0..3`),
  `Beatmap::VARIANT_BY_ID` (`:81+`), `osu-web/app/Libraries/Search/MultiSearch.php:10` and
  `QuickSearch.php:14` (`MODES`), per-mode mod lists in search (`osu-web/app/Libraries/Search/ScoreSearch.php:151-153`),
  convert logic `Beatmap::canBeConvertedTo` (`Beatmap.php:253-255`, osu!standard → others only).
- **Database schema**: per-mode legacy tables `osu_scores`, `osu_scores_taiko`, `osu_scores_fruits`,
  `osu_scores_mania` (+ `_high` variants) and `osu_user_stats`, `osu_user_stats_taiko/_fruits/_mania`
  (+ mania 4k/7k), `osu-web/database/migrations/2015_01_01_133337_base_tables.php:78-794` and later
  migrations; `osu_beatmaps.playmode` int; `solo_scores.ruleset_id` + `solo_scores_performance`
  (`osu-web/database/migrations/2021_03_24_130818_create_solo_scores.php`).
- **Per-mode PHP model classes**: `osu-web/app/Models/Score/{Osu,Taiko,Fruits,Mania,Model}.php` +
  `Score/Best/*`, `osu-web/app/Models/UserStatistics/{Osu,Taiko,Fruits,Mania,Mania4k,Mania7k,Model}.php`,
  `osu-web/app/Models/Traits/Scoreable.php:30`.
- **Routes**: `{mode?}` on `users/{user}/{mode?}`, `me/{mode?}`, `rankings/{mode}/{type}`,
  `rankings/top-plays/{mode?}`, `rankings/ranked-play/{mode?}` (`osu-web/routes/web.php:318-320,376,592-612,630`);
  mode parsed from the route in `osu-web/app/Http/Controllers/UsersController.php:728`.
- **Search/indexing**: ES schemas `osu-web/config/schemas/*.json` (ruleset_id terms), score schema
  versioning in `osu-elastic-indexer`.
- **Frontend**: mode selectors/tabs in the Less/CoffeeScript/TS asset tree (the `ts`/`coffee`/`less`
  files listed in §6) and per-mode transformer classes (`osu-web/app/Transformers/*`).
- Rough scale: ~300 occurrences of the four mode strings across `app/`, `database/`, `config/`,
  `routes/`, and ~93 files matching mode/playmode/MODES patterns (excludes tests and lang files).

In the .NET services (files mentioning modes / `RulesetID`):

| Service | Files | What is hard-coded |
|---|---|---|
| `osu-queue-score-statistics` | 24 | `Stores/StreamedWorkingBeatmap.cs:42-55` constructs `OsuRuleset/TaikoRuleset/CatchRuleset/ManiaRuleset` (i.e. **pp/difficulty needs the four osu! ruleset assemblies**); per-mode hit-stat rules (`Processors/HitStatisticsProcessor.cs:52-63`, catch id 2), medal awarders (`MedalAwarders/*`, mania id 3), `Commands/Maintenance/VerifyScoreRanksCommand.cs:173` switch |
| `osu-server-spectator` | 14 | `Database/Models/matchmaking_pool.cs:63-72` switch 0-3, `LegacyHelper.cs:22` switch, `Database/DatabaseAccess.cs:682` per-mode stats table names |
| `osu-beatmap-difficulty-lookup-cache` | 2 | difficulty calc per ruleset via osu.Game assemblies |
| `osu-elastic-indexer` | 0 | ruleset-agnostic (indexes `ruleset_id` terms) |
| `osu-server-beatmap-submission` | 0 | ruleset-agnostic |

**Estimate for adding a fifth ruleset ("kan") to osu-web**: 100+ files across PHP/TS/C# plus
schema migrations and ES schemas: a new per-mode stats table (or generalising `osu_user_stats`),
a `Score/*` + `UserStatistics/*` model pair, transformer/ranking/search/route/frontend changes, and
on the .NET side a ruleset assembly wiring plus processor changes, with merge conflicts in every
forked file on each upstream update. **pp can be skipped in principle** (kan! leaderboards can rank
by total score), but osu-web assumes pp everywhere: best-score ES queries filter `exists pp`
(`osu-web/app/Libraries/Search/ScoreSearch.php:84`), rankings/stats/medals run through the external
pp processor, and per-mode rank columns live in the per-mode stats tables; each spot needs a fork.
Note also the wire-level clash documented in `lazer-protocol.md` §8.1: kan! reports `ruleset_id 0`,
which osu-web would interpret as osu!standard.

## 4. Level upload

- **There is no web `.osz` upload endpoint in current osu-web.** `BeatmapsetsController` has no
  `store`/upload method (`osu-web/app/Http/Controllers/BeatmapsetsController.php` method list:
  destroy/index/lookup/show/search/discussion/…/download/nominate/love/update/versions). Beatmap
  *content* is not stored in osu-web at all (see §7).
- **Lazer editor upload** goes to `ppy/osu-server-beatmap-submission`: `PUT beatmapsets`,
  `PUT beatmapsets/{id}`, `PUT beatmapsets/{id}/beatmaps/{id}`, `PUT beatmapsets/{id}/versions/{id}`
  (`osu-server-beatmap-submission/osu.Server.BeatmapSubmission/BeatmapSubmissionController.cs:53-540`),
  JWT-validated against osu-web's OAuth public key (`osu-server-beatmap-submission/README.md` step 3,
  `JWT_VALID_AUDIENCE`), storing packages locally or in two S3 buckets (`BEATMAP_STORAGE_TYPE`,
  `S3_CENTRAL_BUCKET_NAME`, `S3_BEATMAPS_BUCKET_NAME`), then notifying osu-web via HMAC-signed
  interop calls to `/_lio/...` (`osu-server-beatmap-submission/osu.Server.BeatmapSubmission/Services/SharedInterop.cs:30-37`).
- **Processing steps** (osu-web side): interop handlers index/broadcast
  (`osu-web/routes/web.php:653-702` `_lio` routes: `index-beatmapset`, `broadcast-new/update/revive`,
  `disqualify`…); archive parsing, **cover extraction** and **preview generation with ffmpeg**
  (`osu-web/app/Models/BeatmapsetArchive.php:17,36-60`, `app/Libraries/ImageProcessor*`), but the
  archive is fetched **from a mirror** (`BeatmapsetArchive::fetch`, config key
  `beatmap_processor.mirrors_to_use`), so upload requires a mirror that already has the file.
- **Metadata required**: parsed from the `.osu` files by the archive/beatmap processor (title,
  artist, creator, difficulty names, mode, audio, background); moderated/ranked flow = hype →
  nominations → qualification → ranked/loved, with beatmap discussions and BNG/NAT-style groups
  (`osu-web/app/Models/BeatmapsetNomination.php`, `BeatmapsetsController@nominate/love`, admin
  `BeatmapsetUnrank`).
- **CLI import of an `.osz` by an admin: not supported.** The artisan commands cover unranking,
  owner migration, ES indexing and cache refreshes (`osu-web/app/Console/Commands/`) but nothing
  ingests an archive. An "import this .osz" workflow would be custom code written against the
  internal processing pipeline (which assumes mirrors + interop + ES), effectively the same work as
  writing our own importer, plus osu-web archaeology.

## 5. Licences and obligations

- **osu-web is AGPL-3.0** (per-file headers "Copyright (c) ppy Pty Ltd … GNU Affero General Public
  License v3.0"; `osu-web/README.md:40`). Running a **modified** osu-web as a public service means
  AGPL §13 applies: users interacting with it over the network must be offered the corresponding
  source. Concretely: a kan!-branded osu-web fork would live in its own repo (e.g.
  `KairoPhos13/kan-web`) **under AGPL-3.0**, and we must publish that source to players. Nothing from
  it may be copied into MIT repositories (licence contamination); the boundary must stay at the API
  level, with the clean-room protocol work of `lazer-protocol.md` remaining MIT.
- **Trademark/branding carve-out**: `osu-web/README.md:42` states the licence "does not cover" use
  of the "osu!" or "ppy" branding, protected by trademark law. A kan! deployment would need to
  replace the wordmark and ppy artwork throughout the UI/lang files (`osu-web/resources/lang/*`,
  `osu-web/public/*`), store items ("osu!supporter"), mail sender defaults (`osu-web/config/mail.php:58`),
  hardcoded asset URLs (`osu-web/config/osu.php:239-253`: `osu.ppy.sh`, `assets.ppy.sh`), and the
  wiki source (pulled from the `osu-wiki` repo). Non-trivial but mechanical.
- **MIT .NET services** (`osu-server-spectator`, `osu-queue-score-statistics`, `osu-elastic-indexer`):
  standard MIT with ppy Pty Ltd copyright, forkable, modifiable, redistributable with attribution.
- **`osu-beatmap-difficulty-lookup-cache` and `osu-server-beatmap-submission` carry no licence file**
  anywhere in the tree and GitHub detects none → by default all rights reserved. We must not copy
  code from them without an explicit grant from ppy. (Running them unmodified as separate services
  is a different question; even that deserves clarification.)
- Fonts/artwork inside osu-web have their own asset licences (not audited here).

## 6. Maintenance cost

- Languages/frameworks: PHP 8.5 + Laravel 13 + Blade + Less + CoffeeScript + TypeScript/jQuery/React
  typings (osu-web); C# / ASP.NET Core (SignalR) for the four .NET services.
- Size: osu-web ~210k lines PHP+Blade + ~22k TS + ~490 Less + 58 CoffeeScript files; the .NET
  services add ~56k lines (spectator 31.7k, score-statistics 16.5k, submission 5.9k, indexer 1.4k,
  lookup-cache 0.6k). A kan!-specific server for goals 1-4 is on the order of a few thousand lines.
- Upstream velocity (12 months to 2026-11-09, GitHub API): **osu-web 2,507 commits** (daily pushes,
  last push same day as this study), spectator 479, score-statistics 158, submission 17,
  lookup-cache 7, indexer dormant since 2025-04. Keeping a fork of osu-web in sync is a continuous
  task (several merges per week); the smaller services are low-churn but pin to osu!-specific
  schema/semantics (e.g. stats table names, medal definitions).
- Operational knowledge required: Laravel + Passport + Octane, ES schema/index management, queue
  workers, cron, S3, mirror operations, plus the pp/difficulty versioning discipline documented in
  `osu-beatmap-difficulty-lookup-cache/README.md` ("the deployed version is critical to producing
  correct results").

## 7. Level file distribution (how osu! serves .osz, covers, previews)

- **`.osz` archives are not served by osu-web; it redirects to download mirrors.**
  `BeatmapsetsController::download` (`osu-web/app/Http/Controllers/BeatmapsetsController.php:177-215`)
  checks `download_disabled`, permissions and a per-user hourly download allowance
  (`BeatmapDownload` rows), picks a `BeatmapMirror` (region-random → default → 503 `no_mirrors`), logs
  the download, and returns `redirect($mirror->generateURL(...))`. The URL is
  `{mirror base_url}d/{beatmapset_id}?fs=…&fd=…&ts=…&cs=checksum&nv=noVideo`
  (`osu-web/app/Models/BeatmapMirror.php:77-95`). The mirror layer is also osu-web's own source for
  archives during processing (`BeatmapsetArchive::fetch` downloads from a mirror,
  `osu-web/app/Models/BeatmapsetArchive.php:36-44`). So the "beatmap storage" is the mirror fleet
  (community-operated in production; a local mirror needs a separate mirror project referenced by
  `osu-server-beatmap-submission/README.md`),
  while osu-web only keeps metadata (MySQL), search state (ES) and derived assets.
- **Covers** are derived assets: extracted/resized on upload (`osu-web/app/Models/Beatmapset.php:176`
  `coverSizes`, `:487-500` `coverURL`/`coverPath`/`storeCover`, stored under
  `beatmaps/{id}/covers/{size}.jpg?{timestamp}` on the default or S3 disk) and served from a static
  asset host / CDN (`StorageUrl::make`, `osu-web/config/filesystems.php:70-147`, with a cache-proxy
  purge hook). Image processing runs in the optional thumbnailer sidecar
  (`osu-web/app/Libraries/ImageProcessorService.php`).
- **Audio previews** are generated from the archive with ffmpeg (44.1 kHz stereo,
  `osu-web/app/Models/BeatmapsetArchive.php:17`), stored on a dedicated `preview` disk
  (`local-preview`/`s3-preview`, `osu-web/config/filesystems.php:93-135`) and exposed at a
  **hardcoded dedicated host**: `https://b.ppy.sh/preview/{id}.mp3`
  (`osu-web/app/Models/Beatmapset.php:511-513`). i.e. covers and previews each live on their own
  asset/CDN hosts, separate from both the website and the download mirrors.
- **What kan! would need to reproduce this**: for our scale (hundreds of levels, hundreds of
  players) nothing as elaborate. The unmodified client follows HTTP redirects on downloads (osu-web
  does exactly that and lazer works), so `GET /api/v2/beatmapsets/{id}/download` can either stream
  from local disk / S3 directly, or 302-redirect to a **pre-signed object-storage URL** (S3/R2)
  when bandwidth grows; both are invisible to the client. Object storage also covers replays later.
  Covers/previews need no hosting at all in the MVP: empty cover URLs are safe in the client
  (`lazer-protocol.md` §8.3), and the client can only load media from `*.ppy.sh` hosts anyway
  (`game/client/osu.Game/Online/TrustedDomainOnlineStore.cs:12-21`) and always fetches previews from
  `b.ppy.sh` (`game/client/osu.Game/Audio/PreviewTrackManager.cs:114`), so on a self-hosted server
  cover art would not render in-game even if we served it, until the client's domain allow-list is
  widened (a small future client change).

## 8. Comparison and recommendation

| Criterion | (A) Adapt osu! server stack (osu-web + .NET services) | (B) Small own server, lazer protocol (NestJS + PostgreSQL) | (C) Hybrid |
|---|---|---|---|
| Effort to first working version | Weeks to months: deploy 8-10 services, mirror, ES, pp pipeline, branding strip, then ruleset surgery | Days to 2 weeks: ~15 endpoints per `lazer-protocol.md`, one DB, one process | B first, then add MIT components (e.g. spectator) on demand |
| Hosting cost / complexity | 4 vCPU/8 GB class VM; 3 datastores; 6+ repos; ops-heavy | 1 small VPS, 1 datastore, docker compose | between A and B |
| Security posture | Mature auth/OAuth (Passport), rate limits, moderation; but huge attack surface, our fork must track security fixes on ~2.5k commits/yr | Small surface we fully understand; must implement our own rate limits/validation carefully | same as B initially |
| Features | Everything (website, rankings, forums, chat, store, tournaments, moderation) + goals 1-4 | Exactly goals 1-4; everything else later | B + selected A-grade pieces |
| Custom ruleset (kan!) | Hard: 100+ files of 0-3 hard-coding, per-mode schema, pp assumptions; `ruleset_id 0` collides with osu!standard semantics | Natural: store ruleset id/name as data; no pp needed (rank by total score) | same as B |
| Maintenance | Continuous upstream merges (osu-web alone: 2.5k commits/yr), AGPL obligations for the web fork | Our code, our pace; protocol drift risk vs client updates only | moderate |
| Licence | AGPL-3.0 (web) + MIT (.NET) + **two unlicensed repos** + trademark scrub | MIT throughout | mixed, boundary-controlled |
| Risk | High upfront + ongoing; ossified around osu! product decisions | Low; can grow incrementally | low to moderate |

**Recommendation (5 lines):** Choose (B), a small own server speaking the lazer protocol, for the
MVP: it reaches goals 1-4 in days, runs on one VPS, stays MIT, and treats rulesets as data instead
of surgery on 100+ hard-coded files. Adopt osu-web only if a full website/community platform becomes
a real goal, and then as a separate AGPL repo (`KairoPhos13/kan-web`) behind our API boundary. Reuse
MIT components selectively later (osu-server-spectator for multiplayer/spectator, ideas from
osu-queue-score-statistics when we add performance metrics). Do not touch the two unlicensed repos
until ppy clarifies terms. The main risk of (B) is protocol drift with client updates, mitigated by
keeping `lazer-protocol.md` as a checked contract with tests.
