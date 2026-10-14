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
  the bridge) or a URL (yt-dlp fetch, "download" progress phase — see §7 for
  the policy).

## 6b. Media & tracks: import in Resources, arrange in the tracks tab

Two surfaces, one rule set (owner requirements 2026-10-13):

**Resources (setup) is the ONLY place that reads files** — audio tracks
(multiple, via the same navigator pattern as background/video), plus the
stock rows for video and images. With "video provides the audio" on, the
video's audio joins the track list automatically (as an accompaniment).
Nothing else anywhere asks for a path: the **track** tab (a first-class
editor tab, second after setup — fork F-11) only *selects and arranges* the
resources imported or created there.

Roles on each track (owner 2026-10-13): **music** (exactly one — the level's
base track, *connected to the editor's Audio Track*; no second import
surface), **reference** (one — the take transcription uses), **accompaniment**
(many — what the render mixes), **demux** (queue). Role-bearing tracks are
highlighted, and the whole set is shown as a list with each track's waveform
as the row background (never just a count).

- **One place to load tracks, one place for video.** The "Add audio track"
  navigator is the single entry point for audio (pick repeatedly for
  multiple tracks); the stock video row is the single entry point for video
  (with "video provides the audio" on, the video's audio joins the track
  list as an accompaniment — the visuals-only case turns the switch off).
- **One unified track list; every track takes a role there:**
  - **reference** (exactly one, optional) — the original take. Transcription
    runs on it automatically (no picker), and the future Mod Auto plays to
    it;
  - **accompaniment** (many) — what the beatmap-audio render mixes;
  - **demux** (queue) — tracks scheduled for source separation.
  Volume and mute stay per track.
- **Demux results rejoin the list** as new tracks and can take any role —
  but **a demuxed track is never demuxable again** (neither is a demux
  result or a rendered mixdown): "hace perder el tiempo".
- **Track editor v1 (non-destructive, owner 2026-10-13):** editing a track
  **creates a NEW track with a user-chosen name** and adds it to the list;
  the source is never modified. Range operations on a track:
  *keep range only* / *silence range* (both preserve original timing — so an
  edited background stays aligned to the song) and *trim to range* (tight
  file, offset lost). The typical case: a section recording two instruments
  (e.g. alto C + bass C ocarinas) yields a cleaned lead track (→ reference,
  transcribed cleanly) and the other instrument's non-overlapping parts as
  background (→ accompaniment). New tracks inherit the material class of
  their source (a stem stays "separated", a mixdown stays "mixed") and
  start with no roles.
- **Waveform previews & meaningful-audio marking:** one analysis pass per
  track (`kan_forge_analyze_track`) feeds a waveform thumbnail on the row —
  activity is visible *before* selecting anything — and the
  **"no meaningful audio"** rule (both peak and RMS below the noise floor).
  After separation, such stems are labelled ("⚠ seems to contain no
  meaningful audio — select only if you are sure") and **moved to the end of
  the list** so they don't mislead; they stay fully selectable (the user may
  know better).
- **Nothing is mandatory:** demux and transcription are independent,
  optional actions — needed in one case, skippable in another. The render
  only needs its accompaniment selection; a reference is only needed if you
  want transcription (or later the Mod Auto).
- **Render:** the audible accompaniments are mixed
  (`ForgeJobs.RenderMixAsync` → `kan_forge_mix_tracks`: gains applied,
  peak-protected) and installed as the beatmap's audio track — playback
  uses exactly what the editor heard.

Verified live in QA (2026-10-13): track import via navigator, transcription
of the reference, render → `audio-render.wav` set as beatmap audio; role
semantics covered by `TrackRolesTest` / `MixdownRuleTest`.

## 7. URL / YouTube policy (owner direction, 2026-10-13)

The URL fetch path uses **yt-dlp**, which brings cookie handling, ToS and
content-claim exposure. Policy of record:

- **Developer-build tooling only:** the URL field is shown only in
  development builds (or with `KAN_DEV_TOOLS=1`). It stays available for the
  developer build — pragmatic convenience, accepted risk at the maintainer's
  own machine.
- **Not part of the official flow:** the shipped product works on **local
  files** (beatmap audio, user-imported tracks). No yt-dlp dependency, no
  download UX, nothing that can raise platform complaints or claims. (That
  other products get away with embedded YouTube access is luck, not a
  licence — we do not bet the product on it.)
- `kan_forge_has_downloader()` lets the UI reflect this honestly.

## 7b. Self-contained separation (DONE, 2026-10-13 — libkan lane)

**The official flow never asks the player to install a separator.** The
requirement of this component (owner 2026-10-13: "libkan handles not having
the model and is compiled to support the flow") was delivered by the libkan
lane as:

1. **Runtime compiled into libkan.** `libkan_roformer` (CMake option
   `KAN_ENABLE_ROFORMER`, default ON) compiles the GGUF RoFormer runtime
   (BSRoformer.cpp + GGML, both MIT) into the library. `kan::separate_stems`
   works in-process with only the auto-downloaded GGUF model.
2. **`find_separator()` probes the RUNTIME**, not just the model: it
   returns `runtime:<model>` when the compiled runtime is present and the
   model is available (auto-downloaded). The external `bs_roformer-cli`
   remains as DEVELOPER TOOLING fallback only (provisional).
3. **Progress honesty:** a failed run never reports 100% (neither the
   in-process path nor the process path).
4. **Input conditioning in the runtime:** any WAV sample rate is linearly
   resampled to the model rate (44.1 kHz); mono is duplicated to stereo.

Status: verified end-to-end (clip separation in-process, 6 stems in
official order; failure semantics covered by code + review). The
game-side CLI pre-check that stood in until then is removed — the bridge
trusts `find_separator()`'s honest probe.

Validation follow-ups (game lane, 2026-10-13):

- **GPU:** `ggml_backend_init_best()` picks the GPU automatically, but the
  backend must be *built* — the game's bridge build enables `GGML_VULKAN`
  (with a CPU fallback when Vulkan tools are absent; `BSR_FORCE_CPU` is the
  runtime escape hatch). **Note for the libkan lane:** the monorepo root
  build still compiles ggml CPU-only — defaulting `GGML_VULKAN` ON there
  (when Vulkan is available) would give every consumer the GPU path.
- **Progress:** the runtime forwards per-chunk fractions correctly; the
  game side now passes its progress sink into the separation job (it was
  silently dropped before, freezing the bar at 0%).

## 8. Remaining slices (mapped to the prompt's acceptance criteria)

| Slice | Covers | Notes |
|---|---|---|
| 2. NLE timeline UI | criteria 2–3: up to 6 stem tracks + source + video track laid out in time; cut/move/delete on any track; video trim (YouTube intros/outros/ads); edits propagate | the track set (volume/mute/import) and the propagation engine are done — this is the timeline UI + a time-based track model; per-track volume/mute UI moves from the section into the track headers |
| 3. Transcription editor UI + persistence | criterion 4: per-note pitch/timing/duration/technique editing, comparison metadata inspection ("unjudged" states), persisting the draft into the level store | `KanDraftNote` already models all fields |
| 4. Environment polish | criterion 5: GPU/CPU auto device + model auto-download flows verified through the UI | mostly verification; the bridge already reports download progress |
