# Font size and UI scaling that stay readable on any screen

Labels: `game`, `good first issue`
Size: S

## Context

kan! is played on a music stand as often as at a desk: a laptop on a
piano, a monitor across a room, a small window next to sheet music.
Reading note names on the staff, timing feedback and settings text must
not require leaning in. osu!lazer has general UI scaling, but kan!'s
own surfaces (the staff with per-note names (letters or Do-Re-Mi), the
notation settings (note size), the editor's dense tables) have never
been checked for readability, and the release goal is a game anyone can
pick up ([README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)).

Readable text is also a first-run issue: the mic test and calibration
screens (see `docs/issues/16` and `docs/issues/17` in `docs/issues/`)
must be legible from a standing player's distance.

## What to do

1. Audit kan!'s surfaces at common resolutions and OS scaling factors
   (1080p 100%/150%, 4K 200%, small laptop windows): the staff and note
   names, judgment/feedback text, HUD meters, settings, editor tabs.
2. Add or expose **font-size / UI-scale controls** where the base
   client's scaling does not reach kan!'s own text (ruleset UI, editor
   screens, first-run screens), and make the defaults comfortably
   readable without setup.
3. Make the staff's note-name size follow the player's setting (the
   notation settings already have a note-size concept,
   `KanNotationSettings`), and verify long note names (e.g. "F♯4") do
   not clip at any supported size.
4. Document the result (a short table: screen → control → range) in
   `game/docs/` or the PR description.

## Acceptance criteria

- [x] Audit results written down (screens checked, resolutions, what was
      unreadable): [docs/accessibility-audit.md](../accessibility-audit.md)
      §3, with a screen → control → range table and sizes at 1080p 100%,
      1080p 150% and 4K 200%
- [x] A text-size/UI-scale control exists for kan!'s own surfaces and
      persists across runs (Settings → kan! → Text size, 75% to 200%,
      also in the notation panel; it composes with osu!'s own UI scale
      and never re-applies it)
- [x] Staff note names legible at the largest supported size from normal
      playing distance; no clipping with sharps/flats (names are
      auto-sized text with no masking anywhere on the staff; "F♯4" and
      "Sol♯4" cannot clip at any size)
- [ ] First-run, calibration and judgment text legible at 1080p 150% and
      4K 200%: judgment text is covered (it follows the control and the
      screen); the first-run and calibration screens of issues 16 and 17
      do not exist yet, and should use the same control when they land
- [ ] Screenshots at two scales in the PR: waiting for the isolated
      instance sweep (`KAN_DEV_NOTATION=text:0.8` / `text:1.5` with
      `KAN_DEV_SCREENSHOTS`)

## Status (2026-11-12)

Done on branch `agent/accessibility`. One control ("Text size",
`KanRulesetSetting.TextSize`, through `KanTextScale`) grows every kan!
surface: the staff's names and labels, the judgment feedback, the markers
and countdown, the wait overlay, the interlude, the practice overlay and
the instrument wedge. It is a factor, not an absolute size, so it rides on
top of whatever the client already does: menu surfaces take it over osu!'s
UI scale (which reaches them through the hierarchy), and gameplay surfaces
take osu!'s UI scale as well because the fork's playfield is deliberately
left out of it (the audit explains the exact chain). The audit's flagged
remainder: the faint guide-line labels' alpha (contrast, not size), editor
tables that follow osu!'s UI scale only, and the screenshots above.

## Pointers

- `KanNotationSettings` (`game/ruleset/`): note name / note size
  settings
- `KanStaff` / `KanPlayfield` (see
  [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md),
  "The screen"): the staff rendering
- osu!lazer's existing scaling settings (the fork's `UserInterface`
  section): extend or complement, keep fork deltas minimal and logged
  in [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)

## Notes

- Good first issue: UI sizing work with clear acceptance points and no
  DSP involvement.
- Pair well with `docs/issues/18` (colour-blind palettes); both are
  "the game must be readable" and can share a settings home.
