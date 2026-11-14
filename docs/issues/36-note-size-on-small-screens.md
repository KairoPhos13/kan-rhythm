# Notes large enough to read on small screens (Steam Deck)

Labels: `game`, `accessibility`
Size: S

## Context

Owner QA of v0.1.1 (2026-11-14): on a Steam Deck (1280×800, 7") the notes
are too small to read even at the largest note size. On a 1080p monitor it
is less serious, but the owner's view is that notes should be larger by
default and the setting should reach much further up.

Today the note size is one ruleset setting
(`KanRulesetSetting.NoteSize`, `game/ruleset/Configuration/KanRulesetConfigManager.cs`):
default 1.2, range 0.8 to 2.0, step 0.05. It is a factor on a size given
in the playfield's own units, so it does not grow with a small physical
screen. Issue [19](19-font-size-and-ui-scaling.md) gave kan!'s text a size
control; note glyphs were left as they were.

## What to do

1. Measure first, in the isolated instance at 1280×800, 1920×1080 and
   2560×1440: the note head's height in pixels at each size step, and
   where notes start to overlap each other, the staff lines or the
   labels. Put the numbers in the PR.
2. Raise the default so a note reads comfortably at arm's length on
   1280×800 (the music-stand case), and widen the range well above 2.0,
   up to where the layout still holds (lanes, staff spacing, note names,
   the hit line). If the layout needs to make room (wider staff spacing,
   fewer visible beats), it does so as the size grows; nothing overlaps.
3. Keep it one setting, applied the same way for every instrument and
   notation.
4. Players who changed the size keep their value; only the default moves.

## Acceptance

- [ ] At the new default, a note head on 1280×800 is clearly larger than
      today's largest size on the same screen (numbers in the PR).
- [ ] The largest size is at least twice today's largest, with no overlap
      at any step (screenshots at three sizes and three resolutions).
- [ ] An offline test covers the size range and the layout's spacing rule.
- [ ] The owner checks it on the Steam Deck.
