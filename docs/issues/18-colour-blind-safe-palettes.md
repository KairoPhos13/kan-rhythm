# Colour-blind-safe palettes for notes and judgment feedback

Labels: `game`, `help wanted`
Size: M

## Context

kan! lets each note carry its own colour, the design tab holds the
level's palette, and judgment is shown with visual states (keep glowing
vs fail morph — see
[game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md)
and [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md)).
Colour is therefore gameplay-relevant information — and for the ~8% of
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
3. Enforce the deeper rule: **never colour alone** — pair colour with
   shape, pattern, or position differences for anything that affects
   judgment. Add a palette contrast check to the design tab/verify tab
   warnings where cheap (e.g. low-contrast note colour on the staff
   background).
4. Test with a simulator (browser extensions or desktop tools simulate
   the three types) and include before/after screenshots.

## Acceptance criteria

- [ ] Written audit: every place colour encodes meaning, listed
- [ ] At least three colour-blind-safe palettes selectable in settings
- [ ] Player palette choice overrides level palette without breaking the
      level's design intent
- [ ] Judgment-critical states differ by more than colour (verified for
      the three CVD types)
- [ ] Before/after screenshots (including simulated CVD views) in the PR

## Pointers

- `KanNotationSettings` (`game/ruleset/`) — note colour / note size
  settings shown before play
- `Kan.Forge.Editor.Design.KanDesignScreen` — level palette,
  `kan.design.json`
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  judgment visuals ("keep glowing vs fail morph")
- [game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md) —
  "Judgment indicators (dual-axis …) — kan! visual vocabulary"

## Notes

- Good first issue territory: palette work is mostly data and settings
  UI, no DSP. Larger "never colour alone" changes can land later —
  say what you could not do.
- Keep the calm tone: this is legibility, not gamification.
