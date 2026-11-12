# Colour-blind-safe palettes for notes and judgment feedback

Labels: `game`, `help wanted`
Size: M

## Context

kan! lets each note carry its own colour, the design tab holds the
level's palette, and judgment is shown with visual states (keep glowing
vs fail morph, see
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
and [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md)).
Colour is therefore gameplay-relevant information, and for the ~8% of
men with colour-vision deficiency, a palette chosen at random can make a
level unreadable. osu!lazer (the client base) already has accessibility
settings culture to build on, but kan!'s own colour semantics (note
colours, palette, judgment states, intonation meters) have never been
checked.

kan! is for everyone who owns an instrument; readable-by-default is part
of "forgiving by default".

## What to do

1. Audit every place colour carries meaning in kan!'s surfaces: note
   colour per note (`KanNotationSettings`), the design tab palette
   (`kan.design.json`, `KanDesignScreen`), judgment/intonation states,
   the staff, editors' waveform/spectrogram overlays.
2. Provide **colour-blind-safe presets** in settings (at minimum
   deuteranopia, protanopia, tritanopia variants of the default
   palette) and make sure level-defined palettes can be overridden by
   the player's choice (the player wins over the level).
3. Enforce the deeper rule: **never colour alone**; pair colour with
   shape, pattern, or position differences for anything that affects
   judgment. Add a palette contrast check to the design tab/verify tab
   warnings where cheap (e.g. low-contrast note colour on the staff
   background).
4. Test with a simulator (browser extensions or desktop tools simulate
   the three types) and include before/after screenshots.

## Acceptance criteria

- [x] Written audit: every place colour encodes meaning, listed
      ([docs/accessibility-audit.md](../accessibility-audit.md))
- [x] At least three colour-blind-safe palettes selectable in settings
      (Settings → kan! → Colour palette: deuteranopia, protanopia,
      tritanopia safe, plus "as the level")
- [x] Player palette choice overrides level palette without breaking the
      level's design intent (backgrounds untouched, lightness kept in the
      role's band, verified over all 13 preset palettes)
- [x] Judgment-critical states differ by more than colour (verified for
      the three CVD types): words, a mark on the judged note ("✓", "↑",
      "↓", "?") and motion; the grades stay 0.10 apart in OKLab under
      every simulated vision
- [ ] Before/after screenshots (including simulated CVD views) in the PR:
      the simulated palette views are in
      [docs/accessibility-palettes.svg](../accessibility-palettes.svg);
      game screenshots wait for the isolated instance sweep
      (`KAN_DEV_NOTATION=palette:…` with `KAN_DEV_SCREENSHOTS`)

## Status (2026-11-12)

Done on branch `agent/accessibility`, with the numbers and the reasoning in
[docs/accessibility-audit.md](../accessibility-audit.md) and every claim
asserted offline by `game/core/tests/KanColourBlindTest.cs` (Machado 2009
simulation, OKLab distances, WCAG contrast). What changed: one setting
(`KanPaletteMode`, in the notation panel and Settings → kan!), twelve note
colours per palette (`KanNoteColours`, the safe tables give the naturals
one hue and the sharps another, with lightness keeping the twelve apart),
the level palette override (`KanPalette.ForMode`), one judgment colour
("meh" moved out of the unknown pitch's grey), the pitch mark on judged
notes, and a contrast warning on the design tab's palette card. What is
left: the screenshots above, and a play-test with colour-blind players
(issues 01 to 06). Suggested follow-ups from the audit: a numeric peak
readout on kan!forge's level meter (its green / yellow / red is the one
remaining colour-primary site), and a decision on the faint guide-line
labels' alpha (0.22) on the staff.

## Pointers

- `KanNotationSettings` (`game/ruleset/`): note colour / note size
  settings shown before play
- `Kan.Forge.Editor.Design.KanDesignScreen`: level palette,
  `kan.design.json`
- [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md):
  judgment visuals ("keep glowing vs fail morph")
- [game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md):
  "Judgment indicators (dual-axis …), kan! visual vocabulary"

## Notes

- Good first issue territory: palette work is mostly data and settings
  UI, no DSP. Larger "never colour alone" changes can land later,
  say what you could not do.
- Keep the calm tone: this is legibility, not gamification.
