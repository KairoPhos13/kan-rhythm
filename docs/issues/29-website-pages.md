# Server MVP follow-up: public website pages (landing + level catalogue)

Labels: `server`, `help wanted`
Size: L

## Context

kan! is about to be published (github.com/KairoPhos/kan-rhythm) and the
client is deliberately offline until kan! has its own servers (fork
F-20: filling `KanEndpointConfiguration` turns the online code back
on). The server design phase is complete —
[docs/architecture/backend-data-model.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/backend-data-model.md),
the OpenAPI contract in `server/api/openapi.yaml`, SQL migrations, a
threat model
([server/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/server/TODO.md))
— and the MVP follow-ups the release needs are the **public-facing web
pages**: a landing page that says what kan! is, and a catalogue where
people browse shared levels.

Two hard rules shape everything here:
- **Clean-room rule (ADR-0012):** `ppy/osu-web` is AGPL-3.0 — emulate
  its UX patterns from public behaviour/screenshots only; **no code
  from it ever enters our repos**. Contributors deep-familiar with
  osu-web source must stay off the corresponding implementation areas.
- **Calm by design:** no engagement mechanics, streaks, or pressure
  loops — the website must not become the thing kan! refuses to be.

## What to do

1. Build the public website MVP against the existing contract-first
   API (`server/api/openapi.yaml`) and data model:
   - **Landing page:** what kan! is ("grab your instrument"), the free/
     calm positioning, how to get the game, how to share levels;
   - **Level catalogue:** browse/search shared levels (song, artist,
     instruments available, difficulty tiers, licence/credits), with a
     download of the `.kan` package;
   - **Level detail page:** credits (composer, arranger, companion
     performers — cover credits are a first-class field), licence,
     per-instrument arrangements, download.
2. Follow the stack chosen in ADR-0007 (implementation language/stack
   of record — check the ADR before starting) and keep the web frontend
   in its planned home (`server/` per the TODO, future `kan-web` repo
   boundary respected).
3. Respect the data model's constraints: catalogue entries come from
   `songs` / `song_versions` / `charts`
   ([backend-data-model.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/backend-data-model.md));
   downloads honour the < 50 KB per-arrangement budget rule.
4. Out of scope for this issue (pending maintainer decisions): account
   sign-up/sign-in flows, emails, hosting and deployment credentials,
   anything payment-related. Design the pages so anonymous browsing
   works regardless.

## Acceptance criteria

- [ ] Landing page renders and states the free/calm positioning and
      getting-started path
- [ ] Catalogue lists levels from the API contract (browse + search),
      anonymous access works
- [ ] Level detail page shows credits, licence, per-instrument
      arrangements and a working download path
- [ ] Clean-room rule respected and stated in the PR description
      ("no osu-web code used")
- [ ] No engagement/compulsion mechanics introduced (calm-by-design
      review in the PR)
- [ ] No auth/emails/hosting work smuggled in — those stay maintainer
      decisions

## Pointers

- [server/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/server/TODO.md) —
  module list (catalog module: `.kan` upload, SHA-256 verification,
  presigned downloads, search)
- `server/api/openapi.yaml` — the contract to build against
- [docs/architecture/backend-data-model.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/backend-data-model.md) —
  §2 modules, §3 data model, §7 client API surface
- [docs/adr/0012-server-leaderboards-osu-reuse.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/adr/0012-server-leaderboards-osu-reuse.md) —
  the clean-room rule
- [docs/adr/0007-server-implementation-stack.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/adr/0007-server-implementation-stack.md) —
  stack of record

## Notes

- Contributors who have read osu-web's source must say so and avoid the
  corresponding areas — the clean-room boundary is honour-based and
  load-bearing.
- The level catalogue is also where `charts/docs/free-song-candidates.md`
  songs and their companions eventually surface publicly; consistent
  credits metadata matters.
