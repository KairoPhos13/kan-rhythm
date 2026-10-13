# kan!forge — level-creation pipeline (editor × libkan): implementation notes

- **Status:** slice 1 implemented and tested (2026-10-13): the libkan C-ABI
  bridge, the background pipeline with live progress, the editor entry-window
  panel (prompt §2.1) and the temporal-edit → transcription propagation with
  tests (prompt §2.2 hard requirement). Slices 2–3 (NLE timeline UI,
  transcription editor UI) are specified below and tracked in
  [../TODO.md](../TODO.md).
- **Of record:** the implementation agent prompt
  `docs/implementation/kan-editor-agent-prompt.md` (monorepo root). This file
  records the engineering decisions it delegated ("decide your dispatch,
  document it") and the shape of what is built.

## 1. Architecture

```
[source audio: beatmap track | URL]          (game/forge/Editor — entry window)
        │
        ▼
Kan.Forge.Pipeline.ForgeJobRunner            background jobs, serialized
        │  P/Invoke (Kan.Forge.Native)
        ▼
libkan_forge_native (game/forge/native)      thin C-ABI adapter, fixed POD
        │
        ▼
libkan (C++): fetch / separate_stems / transcribe_audio
```

- **Thin adapter rule** (prompt §3): all libkan invocation lives behind
  `game/forge/native` (C ABI) + `Kan.Forge.Native` (P/Invoke). Nothing else in
  the game calls libkan — upstream osu! merges stay feasible and the eventual
  live path (NoteEventStream) has one place to land.
- **ABI contract:** `kan_forge_api.h` — fixed-size POD note struct
  (`kan_forge_note`, mirrors `kan::TranscribedNote`), caller-owned buffers,
  status enum, progress callback. Booleans are `int32_t`.
- **Build:** standalone CMake (does not touch the monorepo root build) —
  see [environment.md](environment.md). Offline self-test with synthesized
  input (repo rule 5): `kan_forge_selftest` via ctest.

## 2. Dispatch decision (progress + threading)

- The native progress callback (`kan_forge_progress_fn`) fires **on the worker
  thread** running the libkan job (libkan reports from its own processing and
  from parsing the external separator's output).
- `ForgeJobRunner` marshals every report to the **osu!framework update
  thread** via a captured `Scheduler` (`Scheduler.Add(...)`); UI callbacks are
  plain `Action`s. Headless consumers (tests) may omit the scheduler and get
  inline delivery on the worker thread.
- Jobs are **serialized** (`SemaphoreSlim(1,1)`): the libkan progress hook is
  process-global state, and model downloads / separations must not overlap.
- The native progress delegate is pinned for the process lifetime (the C side
  holds the function pointer).

## 3. Temporal edits → transcription (the hard requirement)

`Kan.Forge.Transcription.TranscriptionDraft` implements the propagation rules
(prompt §2.2: "shifts/splits/removes … hard requirement"). Semantics are
**physically faithful to the audio edit** (v1, tested in
`tests/TranscriptionDraftTest.cs`):

- A note is **split at every segment boundary it crosses**; the piece inside a
  removed/moved segment goes with it; the pieces outside stay where their
  audio stays.
- A piece whose end was cut is flagged `EndFaded` (the end is inferred, not a
  release — same semantics as libkan's `end_faded`).
- Every piece created by an edit is flagged `FragmentedByEdit` so the UI shows
  it as a human-fixable proposal, never as a clean measurement (weak-label
  rule). Whole notes that merely shift stay unflagged.
- Operations: `DeleteRange` (gap stays), `CutRange` (gap closes; later notes
  shift back), `MoveRange` (pieces travel with the segment; the destination
  range is overwritten first), `ShiftOnOrAfter`.
- v1 known limitation: no "close the gap and merge" for a piece that would
  span a closed cut (documented in code); no undo model yet (the editor's
  own undo stack owns that).

## 4. Draft model & "unjudged" semantics (I2)

`KanDraftNote` mirrors the measurements of `kan::TranscribedNote` (onset
anchor, raw end, bias-corrected duration, pitch center + cents, dynamics,
vibrato, `end_faded`, `transition`) and carries the comparison metadata the
judge will need as **nullable** fields (`PitchSaturated`, `Presence`,
`Similarity`): `null` = unjudged/unmeasured, **never wrong** (docs/architecture/
measurement-invariants.md I2). Human edits are tracked per axis
(`PitchEdited`/`TimingEdited`/`ExpressionEdited` → `KanFactState.HumanEdited`).

## 5. Error states (first-class, prompt §3)

| Status | Meaning | Recovery shown to the user |
|---|---|---|
| `ErrFetch` | no downloader / download failed | install yt-dlp hint / retry |
| `ErrSeparator` | bs_roformer-cli or GGUF model missing | env/config hint (`KAN_ROFORMER_CLI`, `KAN_ROFORMER_GGUF`) |
| `ErrDecode` | missing file / unsupported codec | pick another source |
| `ErrTranscribe` | transcription failed | see log |
| `ErrCapacity` | > 65536 notes | report (editor buffer) |
| zero-note result | valid result | "no resolved notes" state, not a crash |

Environment probes (`kan_forge_has_separator` / `has_downloader`) let the UI
disable actions with a hint up front instead of failing halfway.

## 6. Formats

- **Level source of truth:** whatever the kan! ruleset stores (prompt §3,
  docs/architecture/backend-data-model.md) — the draft model is the editable
  working copy in memory for now; persisting it into the beatmap/level store
  is part of slice 3.
- **Interchange (libkan):** chart text (`.score`), MIDI sketch, human log,
  JSONL fact stream — import/export targets, not the native format.
- Source audio: the beatmap's audio track (materialised to a temp file for
  the bridge) or a URL (yt-dlp fetch, "download" progress phase).

## 7. Remaining slices (mapped to the prompt's acceptance criteria)

| Slice | Covers | Notes |
|---|---|---|
| 2. NLE timeline UI | criteria 2–3: up to 6 stem tracks + source + video track; cut/move/delete on any track; video trim (YouTube intros/outros/ads); edits propagate (propagation logic is done — this is UI + a timeline data model) | editor overlay on the osu! editor timeline where possible; `TranscriptionDraft` ops are the propagation engine |
| 3. Transcription editor UI + persistence | criterion 4: per-note pitch/timing/duration/technique editing, comparison metadata inspection ("unjudged" states), persisting the draft into the level store | `KanDraftNote` already models all fields |
| 4. Environment polish | criterion 5: GPU/CPU auto device + model auto-download flows verified through the UI; per-stem playback/enable (chart-transcription-evaluation.md §3d) | mostly verification; the bridge already reports download progress |
