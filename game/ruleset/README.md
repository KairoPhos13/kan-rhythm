# game/ruleset/: the kan! game mode

The kan! game mode, compiled **against** the osu!lazer fork (`../client/`)
and loaded at runtime by the fork's ruleset plugin loader, never built
inside the fork tree (ADR-0010, [osu-integration.md](../../docs/architecture/osu-integration.md)).
How a play works and is judged: [../docs/gameplay.md](../docs/gameplay.md).

## Naming constraints (read before renaming anything)

| Thing | Value | Why |
|---|---|---|
| Assembly / DLL file name | `osu.Game.Rulesets.Kan.dll` | The upstream loader only discovers `osu.Game.Rulesets.*.dll` (`RulesetStore.loadFromDisk`), the one surviving upstream prefix in kan! artifacts |
| C# namespaces | `Kan.Ruleset.*` | ADR-0011: all new kan! code uses `Kan.*` |
| Ruleset short name / display name | `kan` / `kan!` | Product identity |

Inside `Kan.Ruleset` namespaces the bare type name `Ruleset` resolves to
our own namespace; upstream types are spelled fully qualified
(`osu.Game.Rulesets.Ruleset`) on purpose.

## Build & load

`make` (in `game/`) builds and deploys everything. The ruleset's
post-build step copies `osu.Game.Rulesets.Kan.dll`, `Kan.Forge.dll`,
`Kan.Core.dll` and the native bridge next to the client
(`../client/osu.Desktop/bin/<Config>/net10.0/`). For installed builds, the
DLLs go in the game's `rulesets/` storage folder instead (see
[../docs/environment.md](../docs/environment.md) §4).

## Layout

| Path | Content |
|---|---|
| `KanRuleset.cs` | The ruleset: name, mods, the editor tabs it provides, processors |
| `Objects/` | `KanHitObject`: one score note (pitch, duration, style, its pace); `DrawableKanHitObject` asks the live judge and applies its verdict (draws nothing) |
| `Beatmaps/` | `KanBeatmapConverter`: the level's score to hit objects (each note's pace set here); `KanLevelFiles` reads the score and design from a level where no dependency injection reaches |
| `UI/` | The play: `KanPlayState` (split by concern: listening, tries, wait mode, diagnostics), `KanPlayfield`, `KanStaff` (the living staff; its markers and note views in their own files), `KanScene` (the level's design as it plays), the player loader's microphone settings, notation, song select's instrument wedge, the perfect chime |
| `Mods/` | kan!'s mods: Easy, Hard Rock, Hidden, By heart, Wait for me, Practice, … and their score multipliers |
| `Scoring/` | `KanScoreProcessor` (accuracy and rank as in osu!), `KanHealthProcessor` |
| `Replays/` | A replay holds what the judge heard, not inputs (`KanReplayFrame`) |
| `Configuration/` | The player's settings: instrument and range, notation |
| `Dev/` | `KanDevPlay`: developer tooling driven by environment variables; inert without `KAN_DEV_PLAY` (see `../README.md`) |
