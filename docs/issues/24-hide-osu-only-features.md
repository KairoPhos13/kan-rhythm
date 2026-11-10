# Hide osu!-only features still reachable in the client (stable import, tutorial)

Labels: `game`
Size: S

## Context

kan! is a standalone game, not an osu! mod (ADR-0011), and it ships
without osu!'s game modes (fork F-9) and without osu! services (F-20).
But a few **osu!-only features are still reachable** in the UI:
the **osu!stable import** in first-run setup and settings, and the
**osu! tutorial** button. The string sweep deliberately left their copy
because "those features are not kan!'s", see
[String sweep, first pass](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)
and the open release item in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md).

A player pressing "import from osu!stable" in kan! today gets a promise
kan! does not make (kan! plays `.kan` levels, not osu! beatmaps;
conversion is future work in `tools/`), and the tutorial button leads
into osu!'s world. Both should be gone from the release surface.

## What to do

1. **osu!stable import:** remove or hide the entry points (first-run
   setup and settings) so the flow is not reachable in a release build.
   Prefer the smallest fork delta: a guard/hide over deletion (the
   underlying code is upstream's and helps keep rebase cost low; log
   whatever you change in
   [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)).
   Keep the *paths* the importer reads (registry keys, `osu!` folders)
   unchanged if the code stays; they locate a real osu! install
   (F-3 documents this deliberately).
2. **osu! tutorial button:** remove or hide it. If a placeholder is
   wanted, point at kan!'s own "how to play" content when it exists
   (the gameplay doc is the seed:
   [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md));
   a kan! tutorial itself is future work, not this issue.
3. Sweep the surrounding copy for now-dead references (settings
   descriptions, first-run text) and keep the triage rule from the
   string sweep: references to *real* osu! are fine where the feature
   genuinely interoperates; nothing else may imply osu! compatibility.
4. Verify in a running build: the features are not reachable anywhere
   (search the settings overlay and first-run flow), and nothing else
   broke (`make check`).

## Acceptance criteria

- [ ] osu!stable import is not reachable from first-run setup or
      settings in a release build (verified in a running client)
- [ ] The osu! tutorial button is gone (or hidden behind a maintainer-
      decided replacement)
- [ ] Surrounding copy no longer references the removed features
- [ ] Fork deltas minimal, marked per policy, and logged in
      `game/docs/fork-divergence.md`
- [ ] `make check` green in `game/`

## Pointers

- [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md):
  F-3 (stable import paths intentionally unchanged), string-sweep notes
- osu!stable import UI: first-run setup + settings sections in the fork
  (`osu.Game/`)
- [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md):
  the seed for a future kan!-own tutorial
- [docs/adr/0011-rebrand-kan-identity.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/adr/0011-rebrand-kan-identity.md):
  "standalone game, not a mod"

## Notes

- What replaces these features (a kan! level importer, a kan! tutorial)
  is future work decided by the maintainer; this issue only clears the
  release surface.
- Do not delete upstream importer code wholesale; hiding costs less
  merge conflict than deleting.
