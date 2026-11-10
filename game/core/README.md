# game/core/: Kan.Core

What a kan! level is and how a play is judged, free of the game client: it
does not reference osu!, so it builds and is tested on its own
(`cd tests && dotnet test`, under a second).

| Folder | Content |
|---|---|
| `Score/` | `KanScore`: the notes a level is played against (`kan.score.json`, versioned, inside the level); grid following, repeats, bars and beats (`Bars`), credits, MIDI export, the synthesised notes and the perfect chime |
| `Design/` | `KanDesign`: how a level looks (sections, palettes, effects; `kan.design.json`), and `AutoDesign`, which generates one from the notes |
| `Play/` | `LiveJudge`: pairs what libkan heard with the score and grades it (time × pitch ellipses, windows from each note's pace); `ListeningTimeline`: where a heard moment falls on the level; `PlayTake` (the recording kept for a replay), `TuningTracker` (the instrument's tuning), `RangeFit` (fitting a level to the player's range) |
| `Tracks/` | The editor's track set (`ForgeTrack`, `ForgeTrackSet`), its file and undo history, non-destructive edits (`TrackEdits`) and timeline repairs (`TimelineEdit`), track analysis |
| `Transcription/` | A transcription draft (libkan's measurements) and how edits propagate through it |
| `Media/` | FFT, WAV writing, spectral freeze (the wait mode's held sound) |
| `Native/` | P/Invoke to `libkan_forge_native` (`game/forge/native`), the only door to libkan |
| `LevelDocument.cs` | The rule for a level's saved copies: the level's own, or the editor's newer autosave |

The judge's rules, with the reasons behind them, are in
[../docs/gameplay.md](../docs/gameplay.md).
