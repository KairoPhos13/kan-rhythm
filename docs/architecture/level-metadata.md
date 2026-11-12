# Level metadata: length, BPM and star rating

How a kan! level's length, tempo and star rating travel from the level's
files to the places they are shown (song select, the beatmap listing), and
who computes them. It answers a real bug: every level used to show 00:00 and
0.00 stars, because the game read a level's length from the wrong place.

Implementation of record on the game side:
`game/core/Score/LevelMarks.cs` and `game/core/Score/LevelDifficulty.cs`;
on the server side: `server/app/src/beatmaps/level-difficulty.ts` and
`server/app/src/beatmaps/osu-file.ts`.

## What a level carries

A level is an osu! archive (`.osz`) whose extra files carry what the osu!
format cannot:

| File | What it holds |
|---|---|
| `<Artist> - <Title> (Creator) [Version].osu` | the osu! side: metadata, the tempo grid, the background, and the score's **marks** (below) |
| `kan.score.json` | the **score**: every note's timing, pitch, duration and playing style |
| `kan.design.json`, `kan.credits.json`, `CREDITS.md` | the look, the credits |

The osu! file says `Mode: 0` and the kan! ruleset claims legacy ruleset id 0
(`KanRuleset.LegacyID`, see osu-integration.md §3): that is how the decoder
knows the level is kan!'s. There is nowhere to put a non-legacy ruleset id:
the osu! format stores only the `Mode` integer, and the encoder refuses ids
outside 0-3. So `Mode: 0` is the level's ruleset marker and stays as it is.

## The marks: the score's span in the .osu

The notes cannot travel in the `.osu` (the osu! format has no pitch and no
note duration), but the game and the server both read a level's length and
object count from its hit objects. So the `.osu` carries a **mark** per note:

- one plain circle at every note's start, and
- one plain circle at the level's end (the last note's end).

That is the whole span the level is played over. The ruleset replaces the
marks with the real notes when the level is played (`KanBeatmapConverter`),
and plain circles are deliberate: a circle carries only a time, which is
exactly what survives the editor's decode-and-encode round trip of a kan!
level untouched, so a level's length cannot drift with every save.

Who writes the marks:

- **tools/level_pack** writes them when a starter level is built
  (`kanpack/level.py`, `osu_text`). `make_level.py osu` rewrites only the
  `.osu` from the score source, leaving reviewed audio bytes alone.
- **kan!forge** keeps them in step with the score every time the score is
  saved into the level (`LevelOsuMarks.Sync`), including the editor's own
  copy of the objects so a later editor save writes the same marks.
- **The game** repairs levels made before the marks existed, once, quietly
  (`KanMarksMigration`): a level whose length reads as unknown gets its marks
  and is reprocessed.

A level whose score has no notes keeps whatever its `.osu` carries: a level
being built from an imported song may still rely on its original objects.

## How the numbers are computed

At import (and at every editor save) the game's `BeatmapUpdater.Process`
fills `BeatmapInfo` from the level's files:

- **Length** = the playable span of the decoded hit objects (the marks):
  from the first mark to the last one's end. This is the game's own rule
  (`BeatmapInfoExtensions.UpdateStatisticsFromBeatmap`), unchanged.
- **BPM** = the tempo grid's most common beat length, unchanged. (The marks
  do not affect it; a kan! level has one tempo grid.)
- **Star rating** = the ruleset's difficulty calculation, below. When
  `KanDifficultyCalculator.Version` moves on, the game recomputes the star
  rating of every kan! level a player already has
  (`BackgroundDataStoreProcessor.clearOutdatedStarRatings`).

The server stores the same numbers for the beatmap listing (`importer.ts`):
`total_length` from the marks' span (first mark to last end, as the game
computes its length) and `difficulty_rating` from the same formula.

## The star rating: pace and reach

kan!'s star rating is a simple, honest model of the two things the player
actually meets: how fast the notes come and how far the melody reaches.
Whatever instrument plays them is treated the same; nothing here is one
instrument's special case.

From a level's notes:

- `notesPerSecond` = note count / span, where the span is from the first
  note's start to the last note's end, measured as at least one second of
  music (a lone short note must not read as frantic);
- `pitchSpanSemitones` = from the lowest to the highest known pitch (notes
  whose pitch is only a guess say nothing about the reach), in semitones;
- `stars = 0.2 + 1.2 * notesPerSecond + 0.8 * (pitchSpanSemitones / 12)`,
  and 0 for a level without notes.

So one note per second adds 1.2 stars, one octave of reach adds 0.8 stars,
and 0.2 is the floor of a level that has notes. The starter pack's calm
levels read about 2 to 3 stars. A level without a score (an imported osu!
beatmap) gets its pace from its hit objects and no reach: the marks carry no
pitch.

The formula lives in `game/core/Score/LevelDifficulty.cs` and is mirrored in
`server/app/src/beatmaps/level-difficulty.ts`; the two are pinned together by
mirrored tests (`LevelDifficultyTest` and `level-difficulty.spec.ts`). When
it changes, change both and bump `KanDifficultyCalculator.Version`, so the
levels players already have are recomputed.

This is the model of the moment, not the final one: the `kp`-era difficulty
work (chart features, instrument family, per-mod attributes, ADR-0012) is a
later milestone. Until then the difficulty calculation computes no skills and
no performance attributes, and the star rating is a level's own (it does not
change with mods).

## What was considered instead

A fork change could teach the game to ask the ruleset for a level's extent
(for example a hook in `BeatmapInfoExtensions.UpdateStatisticsFromBeatmap`,
which computes `BeatmapInfo.Length` from the decoded hit objects and nothing
else). It was not made: the marks keep the fork untouched and work for the
server and any other reader of an `.osu` too. If the marks are ever dropped,
that hook is the fork change to make: `osu.Game/Beatmaps/BeatmapInfoExtensions.cs`,
`UpdateStatisticsFromBeatmap`, falling back to the ruleset's answer for a
beatmap whose hit objects do not cover its level.

## What existing levels get

- **The starter pack** is re-rendered with marks
  (`tools/level_pack/make_level.py osu`), and its pack version stays the
  same: a changed `.osu` changes the archive's hash, and the game would
  import a changed pack as a second copy of each level rather than as an
  update. The repair below covers the copies players already have.
- **Levels already imported** (starter pack or made in kan!forge) get their
  marks from `KanMarksMigration` at startup: quiet, background, one pass,
  and it only looks at levels whose length is unknown.
- **Star ratings** of all existing levels are recomputed by the game itself
  when `KanDifficultyCalculator.Version` changes (see above).
