# Server MVP follow-up: public profile pages (kp stats, per-instrument ranks)

Labels: `server`
Size: M

## Context

The server design is complete: data model
([docs/architecture/backend-data-model.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/architecture/backend-data-model.md)
— `users`, `kp_stats`, `leaderboard_entries`, `scores`), the `kp`
formula (ADR-0008: rhythmic precision + intonation purity, weighted
top-N), the ranked-status workflow (ADR-0009) and the osu! reuse policy
(ADR-0012). The MVP follow-ups for the release include **profile pages**
— the public face of a player's progress: their `kp`, their per-instrument
ranks, their recent plays.

Profiles must be built the kan! way. The client is "forgiving by
default … never aims at anxiety or compulsion"
([README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md));
a profile page that shames, compares compulsively, or turns practice
into a grind would betray that. And the scope boundary is firm: how
accounts work is a pending maintainer decision — this issue is the
**public display** of profile data.

## What to do

1. Build the public profile page against the existing contract
   (`server/api/openapi.yaml` has profiles and boards) and data model:
   - identity block (display name, chosen instruments, join date);
   - `kp` summary: total, per-instrument-family ranks per ADR-0008's
     weighting;
   - recent plays / best plays per level (score, timing + intonation
     breakdown — the dual axis is the point of `kp`;
     [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md)
     shows what a play contains);
   - a plain "levels created" block (links into the catalogue from
     `docs/issues/29`).
2. Presentation principles (part of the acceptance criteria, not
   decoration):
   - no streaks, no "you are falling behind", no forced comparisons;
   - ranks shown plainly, context not pressure; the player's own
     history is the hero, not the leaderboard;
   - honest numbers: dual-axis (timing vs cents) shown as information
     for practice, not as a verdict.
3. Keep implementation within the boundary: anonymous read-only pages;
   sign-in, accounts, emails and hosting are pending maintainer
   decisions and out of scope. Data comes from `kp_stats` /
   `leaderboard_entries` / `scores` per the data model.

## Acceptance criteria

- [ ] Profile page renders from API data: identity, `kp` summary,
      per-instrument-family ranks, recent/best plays
- [ ] Dual-axis (timing + intonation) breakdown visible per play
- [ ] Levels-created block links to catalogue entries
- [ ] Calm-by-design review passed: no streaks, shame, or compulsion
      mechanics (evidence in the PR description)
- [ ] Anonymous read-only access works; no auth/email/hosting work
      included
- [ ] Matches `server/api/openapi.yaml` (contract-first rule)

## Pointers

- [docs/architecture/backend-data-model.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/architecture/backend-data-model.md) §4 —
  leaderboard semantics; §3 data model
- [docs/adr/0008-kp-formula.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/adr/0008-kp-formula.md) —
  what `kp` means and how it mixes the two axes
- [docs/adr/0012-server-leaderboards-osu-reuse.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/adr/0012-server-leaderboards-osu-reuse.md) —
  clean-room rule if any web UX is borrowed from public patterns
- `server/api/openapi.yaml` — profiles/boards endpoints
- [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md) —
  what a play/judgement contains (replays, traces)

## Notes

- Cheating is accepted as out of scope for now (owner position in
  [game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md));
  do not build anti-cheat UI promises the backend does not keep.
- If the profile schema needs fields the data model lacks, propose them
  in the data model doc first (contract-first rule).
