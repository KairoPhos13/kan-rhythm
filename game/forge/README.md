# game/forge/ — kan!forge (Kan.Forge)

The level editor's own tabs and the audio played along a level, on top of
[Kan.Core](../core/). Implementation notes:
[../docs/editor-pipeline.md](../docs/editor-pipeline.md).

| Folder | Content |
|---|---|
| `Editor/` | The editor tabs kan! adds to osu!'s: **tracks** (`KanTracksScreen`: arrange, mix, separate, transcribe, render the level audio), **compose** (`Compose/`: the piano roll, tempo grid, timeline repairs, copy and paste), **design** (`Design/`), and the setup tab's sections (resources, instrument, cover credits). `LevelSession` opens the level for all of them (tracks, then score, then design); `ScoreSession`, `DesignSession` and `ForgeTrackSession` hold what is being edited; `LevelFile` reads and writes kan!'s files inside the level. Shared widgets (`ForgeButton`, `RoleChip`, `TagPill`, …) give every surface one look. |
| `Audio/` | `AudioChannel` — one track played along the level (its own mixer for filters and gain), and `ChannelSync`, which keeps it on the timeline without audible corrections. Every sound kan! plays with a level goes through them. |
| `Play/` | Pieces of the game that live outside the ruleset: the live session on the microphone (`LiveSession`), the parts heard along a level (reference, notes, take, the song select preview), the wait mode's held sound, mic delay measurement |
| `Media/` | Decoding and processing with BASS: audio from video, filters, pitch shift, retiming; spectrograms; recording |
| `Pipeline/` | `ForgeJobRunner` — background jobs (separation, transcription) with progress reported to the UI thread |
| `Tracks/` | `ForgeTiming` — the player's timing settings (mic delay, timing habit, instrument tuning), saved in `timing.json` |
| `native/` | `libkan_forge_native`: the thin C ABI over libkan (`kan_forge_api.h` is the contract) and its self-test; its own CMake project |
| `tests/` | NUnit tests that need the game client (audio, media, sessions); the rest live in `core/tests` |

## Ground rules

- **libkan describes, the game judges** — measurements are forwarded, never
  re-derived from raw audio (docs/architecture/measurement-invariants.md).
- libkan is reached **only** through `native/` and `Kan.Core.Native`.
- Background jobs never touch the editor's state directly: they hand their
  results to the update thread (the UI reads it every frame).
- The native ABI is fixed-size POD; no allocations cross the boundary.
