# game/ruleset/ — the kan! ruleset plugin (Phase 0 skeleton)

The kan! game mode, compiled **against** the osu!lazer fork (`../client/`)
and loaded at runtime by the fork's ruleset plugin loader — never built
inside the fork tree (ADR-0010, [osu-integration.md](../../docs/architecture/osu-integration.md)).

**Status:** Phase 0 skeleton. It proves the plugin path and shows a
placeholder screen; there is no gameplay yet. The real work (dual-axis
judgment, highway, instrument helpers) is listed in
[../TODO.md](../TODO.md) under "Input & judgment core".

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
| `Objects/KanHitObject.cs` | placeholder hit object (start time only) |
| `Objects/DrawableKanHitObject.cs` | empty drawable; placeholder auto-resolve (NOT judgment) |
| `Beatmaps/KanBeatmapConverter.cs` | 1:1 timing-only conversion (placeholder) |
| `Difficulty/KanDifficultyCalculator.cs` | zero-skill placeholder |
| `UI/KanDrawableRuleset.cs`, `UI/KanPlayfield.cs` | placeholder screen |
