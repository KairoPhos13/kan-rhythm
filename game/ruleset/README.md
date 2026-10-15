# game/ruleset/ — the kan! ruleset plugin

The kan! game mode, compiled **against** the osu!lazer fork (`../client/`)
and loaded at runtime by the fork's ruleset plugin loader — never built
inside the fork tree (ADR-0010, [osu-integration.md](../../docs/architecture/osu-integration.md)).

**Status:** gameplay MVP (2026-10-14, [../docs/gameplay.md](../docs/gameplay.md)):
a level plays its score (`kan.score.json`), the microphone is described live
by libkan through the bridge, the game judges each note (timing + the right
note) and draws the living staff. Remaining judgment work (sustain and
technique scoring, latency budget) is in [../TODO.md](../TODO.md) under
"Input & judgment core".

## Naming constraints (read before renaming anything)

| Thing | Value | Why |
|---|---|---|
| Assembly / DLL file name | `osu.Game.Rulesets.Kan.dll` | The upstream loader only discovers `osu.Game.Rulesets.*.dll` (`RulesetStore.loadFromDisk`) — the one surviving upstream prefix in kan! artifacts |
| C# namespaces | `Kan.Ruleset.*` | ADR-0011: all new kan! code uses `Kan.*` |
| Ruleset short name / display name | `kan` / `kan!` | Product identity |

Note: inside `Kan.Ruleset` namespaces the bare type name `Ruleset` resolves
to our own namespace — upstream types are spelled fully qualified
(`osu.Game.Rulesets.Ruleset`) on purpose.

## Build & load

```bash
dotnet build -c Release          # also copies the DLL next to the client
```

The post-build step drops `osu.Game.Rulesets.Kan.dll` into
`../client/osu.Desktop/bin/<Config>/net10.0/` (dev convenience). For
installed builds, put the DLL in the game's `rulesets/` storage folder
instead (see [../docs/environment.md](../docs/environment.md) §4).

## Layout

| File | Content |
|---|---|
| `KanRuleset.cs` | ruleset metadata + plugin entry (name "kan!", tagline "You kan!") |
| `Instruments/KanInstrument.cs` | instrument catalog: category ↔ instrument tie, display names (provisional taxonomy) |
| `Configuration/KanRulesetSetting.cs`, `Configuration/KanRulesetConfigManager.cs` | persisted selection (client.realm — remembered across runs) |
| `UI/KanSettingsSubsection.cs` | v0 instrument selector (category-tied dropdowns) in Settings → Rulesets → kan! |
| `Objects/KanHitObject.cs` | one score note (pitch, duration, style) + judgement and hit windows |
| `Objects/DrawableKanHitObject.cs` | judging side of a note: asks the live judge, applies the result (draws nothing) |
| `Beatmaps/KanBeatmapConverter.cs` | the level's score → hit objects; timing-only placeholders without a score |
| `Beatmaps/KanLevelFiles.cs` | reads the score from the level (or the editor's newer autosave); the ruleset icon hands it the game storage |
| `Difficulty/KanDifficultyCalculator.cs` | zero-skill placeholder |
| `UI/KanDrawableRuleset.cs` | drawable ruleset + input manager (replay recording; no key bindings) |
| `UI/KanPlayState.cs` | live session, judge, pitch trail, bloom; one log line per play |
| `UI/KanPlayfield.cs`, `UI/KanStaff.cs` | background + the living staff; the bloom's low-pass on the music |
| `Scoring/KanHealthProcessor.cs` | no game over |
| `Dev/KanDevPlay.cs` | developer tooling (env vars): play a level unattended, recording as input, screenshots |
