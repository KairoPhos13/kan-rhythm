# String sweep, remaining scope: leftover "osu!" text in English strings

Labels: `game`, `good first issue`
Size: S

## Context

The first English string pass is done (2026-10-27): the game's own name
in strings that mean *the game itself* now says kan! (first-run setup,
settings, folders, fullscreen, exit dialog, update/audio notifications,
editor title, web user agent — each delta marked in
`Localisation/*Strings.cs`). The remaining scope is listed at the bottom
of [game/docs/fork-divergence.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/fork-divergence.md)
("String sweep — remaining scope") and is still a release item in
[game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md):
some English text still says "osu!" where it means our game.

Translating other languages is tracked separately
(`docs/issues/23`), and the osu!-only features themselves are
`docs/issues/24`. This issue is the **mechanical English sweep**.

## What to do

1. Work through the remaining-scope list in
   [game/docs/fork-divergence.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/fork-divergence.md):
   - `osu.Desktop/NVAPI.cs` — NVIDIA profile names;
   - `osu.Desktop/Windows/WindowsAssociationManager.cs` +
     `osu.Game/Localisation/WindowsAssociationManagerStrings.cs` —
     strings still naming osu!'s file types/link scheme (the features
     are not installed since F-20; only the text is in scope here);
   - any other user-visible English string where "osu!" means *kan!
     itself* — sweep `osu.Game/Localisation/*Strings.cs` and dialog
     copy; use `git grep -i "osu!"` inside the fork and triage each hit.
2. Triage rule: replace when "osu!" means this game; **keep** when it
   refers to actual osu! (e.g. "import from osu!stable" — that feature
   is scoped in `docs/issues/24`) or is third-party attribution
   ("Built on osu!lazer (MIT) — ppy/osu contributors" stays).
3. Every fork-file delta gets the `kan! rebrand (ADR-0011)` marker
   comment and an entry/update in the fork-divergence log (ask a
   maintainer to merge the log update; follow the file's policy).
4. Two items in the list are **product decisions, out of scope here** —
   note them in the PR and stop:
   - `OSU_PROTOCOL = "osu://"` (keep for compatibility vs move to
     `kan://`) — pending maintainer decision;
   - kan!'s own file types and link scheme naming — pending maintainer
     decision.

## Acceptance criteria

- [ ] `git grep -i` over the fork shows no user-visible English string
      where "osu!" means kan! itself
- [ ] NVAPI profile names handled (renamed to kan!-neutral names)
- [ ] Attribution and genuine-osu! references deliberately kept, each
      hit triaged and listed in the PR
- [ ] Every changed fork file carries the ADR-0011 marker comment
- [ ] The two maintainer-decision items left explicitly flagged, not
      changed

## Pointers

- [game/docs/fork-divergence.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/fork-divergence.md) —
  "String sweep, first pass" and "remaining scope" (the checklist of
  record)
- `osu.Game/Localisation/*Strings.cs` — the string classes (deltas
  marked per file)
- [docs/adr/0011-rebrand-kan-identity.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/adr/0011-rebrand-kan-identity.md) —
  the rebrand policy
- Build check: `make check` in `game/` (warnings-as-errors + tests)

## Notes

- Good first issue: mostly mechanical text changes in one submodule —
  read the fork-divergence policy first (5 minutes) and the rest is
  careful grepping.
- Minimize fork divergence: do not reword beyond the name swap; each
  extra character is a future merge conflict.
