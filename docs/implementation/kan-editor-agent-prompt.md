# Implementation Agent Prompt — KAN Level Editor × libkan

> **How to use:** give this document to the implementation agent working in
> `game/` (the KAN client). It is self-contained: what to build, under
> which constraints, against which contracts, with acceptance criteria.
> Everything referenced here exists in this repository today.

---

## Prompt

You are implementing the **level-creation pipeline of KAN**, a rhythm game
for real instruments. KAN is a fork of **ppy/osu** (osu!lazer, MIT). Work
inside `game/`.

### 0. Ground rules (non-negotiable)

1. **KAN is a fork of osu!lazer**: preserve upstream attribution
   (CONTRIBUTING.md §6), keep fork divergence minimal (AGENTS.md rule 2),
   and follow `docs/adr/0010` + `docs/architecture/osu-integration.md`
   for the ruleset integration. The product name is **KAN** (the osu
   name no longer appears in user-facing surfaces you build).
2. **libkan describes, the game judges** — libkan (C++) emits only
   measurements ("facts"). Never re-derive verdicts from raw audio in the
   game. Contracts to obey:
   - `docs/architecture/measurement-invariants.md` (I1–I12, T1–T3)
   - `docs/architecture/game-judging-guide.md` (G1–G6)
   - `docs/architecture/note-description-stream.md` (fact stream, JSONL v3)
3. Build must stay clean (no new warnings), tests green, all repo content
   in English. Feature work needs tests (repo rule 5).
4. Heavy AI dependencies are **optional layers**; the shipped inference
   stack is **GGUF + Vulkan** (auto device: GPU when present, else CPU).
   See `docs/architecture/chart-transcription-evaluation.md` (single
   stack decision, model acquisition, progress protocol).

### 1. What exists in libkan (your integration surface)

Header-only core (`libkan/include/libkan/`), C++20. Key APIs:

- `kan::transcribe_audio(sig, n, sr, profile) -> vector<TranscribedNote>`
  — offline transcription: one record per struck note with onset (audio
  anchor), bias-corrected duration (+ raw end + `end_faded`), pitch
  center + cents, dynamics dB, vibrato rate/depth.
- `kan::separate_stems(audio, out_dir) -> stem_dir` — source separation
  (single stack: IN-PROCESS GGUF runtime compiled into libkan via
  `libkan_roformer` — no installed separator; see game/docs/editor-pipeline.md
  s7b. Any WAV rate accepted; progress via the same contract). Produces stems named
  `bass/drums/guitar/other/piano/vocals.wav` (official order). Models
  auto-download to `models/` (`KAN_ROFORMER_GRADE`: q8 default, f16,
  q4; `KAN_MODEL_URL` switches to our own distribution later).
- `kan::set_progress(fn, user)` — **the progress contract** (a method,
  not console scraping): callback `(phase, percent)` with phases
  `download`, `separate`, `transcribe`. kan_demo demonstrates the
  consumer pattern (apps/kan_demo/main.cpp `console_progress`).
- `kan::chart_text(notes)` / `kan::chart_log(notes)` / `kan::chart_midi(notes)`
  — chart serialization (parse_score-compatible text with `#` annotations,
  MIDI sketch, human-readable log).
- `kan::NoteEventStream` + `NoteDescriber` — the live fact stream
  (HIT/PITCH/CONTENT/EXPRESSION/CLOSE; EXPRESSION carries dyn tier,
  vibrato, bend, envelope). For later live judging; the editor uses the
  offline APIs first.

Reference consumers (working examples): `tools/dataset_bench`,
`tools/chart_scribe`, `tools/stem_split`, `apps/kan_demo --transcribe`
and `--guide`.

### 2. Scope — the editor flow (this phase: connect libkan with KAN)

**Phase goal: the creation pipeline, not the gameplay visualization.**
Note rendering inside the level comes LATER; do not build it now.

#### 2.1 Entry window (extend the existing osu! creation screen)

The user fills the current fields as today, plus:

- **[ ] Separate audio stems** — a button/toggle that starts separation
  in the background with a **progress bar**; the user keeps filling the
  form meanwhile. (Phases come from `set_progress`.)
- **[ ] Generate transcription** — same pattern: background work +
  **progress bar** (its own phase).
- Both feed from the chosen source audio (local file or URL — see
  kan_demo's fetch path for the URL case; downloads report `download`
  progress).

#### 2.2 Timeline editor (stems + video tracks)

NLE-style multi-track editor:

- **Audio tracks**: each generated stem (up to 6) as its own channel —
  or a single source-audio track when separation is not used. Track
  volume/mute per channel (game-editor idea recorded in
  chart-transcription-evaluation.md §3d: e.g. enable an accompaniment
  stem only while the lead instrument is silent).
- **Video track**: background image or video (osu! already supports
  background visuals — extend to VIDEO sources). Real use case: YouTube
  videos with intros/outros/ads **intercalated** — the user trims those
  segments out of the level. Extensible to arbitrary clips.
- **Editing ops**: cut segments, move them, delete them — on any track.
- **Temporal edits propagate to the transcription**: cutting/moving an
  audio segment shifts/splits/removes the transcribed notes that fall in
  it (the transcript is anchored to audio time; keep it consistent —
  this is a hard requirement, not a nicety).

#### 2.3 Transcription editor

Per-note editing of the transcribed draft:

- **Note**: pitch (name/octave), onset, duration.
- **Playing characteristics**: vibrato (rate/depth), bend, dynamics tier —
  the EXPRESSION facts; editable because the draft is a draft (weak-label
  rule: AI/measurement output is proposal, human confirms).
- **Comparison metadata**: everything the judge will use later must be
  visible/editable or at least inspectable: onset anchor (= audio attack),
  bias-corrected duration + raw end + `end_faded`, `saturated` pitch flag,
  presence/sim values. Treat unmeasurable fields as "unjudged", never as
  wrong (I2 semantics).

### 3. Design notes

- Background jobs run the libkan offline APIs on worker threads; marshal
  progress to the UI thread via the callback (the callback contract is
  thread-agnostic — decide your dispatch, document it).
- Keep libkan invocation behind a thin adapter in `game/` (so upstream
  osu! merges stay feasible — minimal divergence rule).
- File formats: source of truth for levels is whatever the KAN ruleset
  stores (see `docs/architecture/backend-data-model.md`); the chart text
  (`.score`) and JSONL facts are libkan interchange formats — import/
  export them, don't force them as the native format.
- Error states are first-class: missing model (download failed), missing
  bs_roformer binary, unsupported audio codec, transcription with zero
  resolved notes. Each needs a human-readable message and a recovery.

### 4. Acceptance criteria

1. From the entry window, a user can (a) separate stems, (b) generate a
   transcription, each with a live progress bar driven by the libkan
   progress API, while continuing to use the form.
2. The timeline editor shows up to 6 stem tracks + source audio + a video
   track; cut/move/delete work on segments; the video trim use case
   (cutting intros/outros/ads from a YouTube recording) is demonstrated.
3. Editing the timeline updates the transcription consistently
   (shift/split/remove notes with their audio segments).
4. The transcription editor can change pitch/timing/duration, technique
   facts, and inspect comparison metadata; unmeasurable fields show as
   "unjudged".
5. All of the above works with GPU and CPU (automatic device), with the
   model auto-downloaded on first use.
6. New code compiles warning-free; ctest suite stays green; English-only
   content; attribution intact.

### 5. References (read before coding)

- `docs/architecture/game-judging-guide.md` — what the judge needs later
  (shapes the metadata you expose now)
- `docs/architecture/chart-transcription-evaluation.md` — Layer 0/1
  decisions, single stack, model acquisition, progress protocol
- `docs/architecture/measurement-invariants.md` — the balance not to break
- `tools/dataset_bench/TEST_LOG.md` — measured behaviors and failure
  modes (why the metadata exists)
- `apps/kan_demo/main.cpp` — working end-to-end reference
  (`--transcribe`, `--guide`, progress consumer)
