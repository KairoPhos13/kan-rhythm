# kan!forge, level-creation pipeline (editor × libkan): implementation notes

- **Status:** slice 1 implemented and tested (2026-10-13): the libkan C-ABI
  bridge, the background pipeline with live progress, the editor entry-window
  panel (prompt §2.1) and the temporal-edit → transcription propagation with
  tests (prompt §2.2 hard requirement). Slices 2-3 (NLE timeline UI,
  transcription editor UI) are specified below and tracked in
  [../TODO.md](../TODO.md).
- **Of record:** the implementation agent prompt
  `docs/implementation/kan-editor-agent-prompt.md` (monorepo root). This file
  records the engineering decisions it delegated ("decide your dispatch,
  document it") and the shape of what is built.

## 1. Architecture

```
[source audio: beatmap track | URL]          (game/forge/Editor: entry window)
        │
        ▼
Kan.Forge.Pipeline.ForgeJobRunner            background jobs, serialized
        │  P/Invoke (Kan.Core.Native)
        ▼
libkan_forge_native (game/forge/native)      thin C-ABI adapter, fixed POD
        │
        ▼
libkan (C++): fetch / separate_stems / transcribe_audio
```

- **Thin adapter rule** (prompt §3): all libkan invocation lives behind
  `game/forge/native` (C ABI) + `Kan.Core.Native` (P/Invoke). Nothing else in
  the game calls libkan; upstream osu! merges stay feasible and the eventual
  live path (NoteEventStream) has one place to land.
- **ABI contract:** `kan_forge_api.h`, fixed-size POD note struct
  (`kan_forge_note`, mirrors `kan::TranscribedNote`), caller-owned buffers,
  status enum, progress callback. Booleans are `int32_t`.
- **Build:** standalone CMake (does not touch the monorepo root build);
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

`Kan.Core.Transcription.TranscriptionDraft` implements the propagation rules
(prompt §2.2: "shifts/splits/removes … hard requirement"). Semantics are
**physically faithful to the audio edit** (v1, tested in
`tests/TranscriptionDraftTest.cs`):

- A note is **split at every segment boundary it crosses**; the piece inside a
  removed/moved segment goes with it; the pieces outside stay where their
  audio stays.
- A piece whose end was cut is flagged `EndFaded` (the end is inferred, not a
  release, same semantics as libkan's `end_faded`).
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
  docs/architecture/backend-data-model.md): the draft model is the editable
  working copy in memory for now; persisting it into the beatmap/level store
  is part of slice 3.
- **Interchange (libkan):** chart text (`.score`), MIDI sketch, human log,
  JSONL fact stream, import/export targets, not the native format.
- Source audio: the beatmap's audio track (materialised to a temp file for
  the bridge) or a URL (yt-dlp fetch, "download" progress phase; see §7 for
  the policy).

## 6a. Glossary (naming convention, owner 2026-10-14)

Use these words everywhere (UI, code, docs, saved files):

| Term | Meaning |
|---|---|
| **set** | all tracks of a level (`ForgeTrackSet`), saved with the level |
| **companion** (role, many) | plays under the player during the level (`IsCompanion`) |
| **level audio** | the beatmap's single audio file (osu!'s "Audio Track") = the mix of the companion tracks, as heard, cut to its start/end (`AudioStartMs/AudioEndMs`). Re-rendered automatically when the companion mix changed (leaving the track tab, saving, closing the Resources picker); there is no manual render button. A file is still needed: gameplay and the other tabs play one audio file |
| **loudness** | every level audio is levelled to the game's reference, **−20 LUFS** (ITU-R BS.1770, `libkan/loudness.hpp`), its peaks held under −1 dBFS: 0 dB, automatic. The mix desk shows where it sits and sets it ±1 dB at a time from the reference, up to ±12 (`ForgeTrackSet.LoudnessOffsetDb`, saved, undoable). Owner 2026-10-23: eight levels spread over 12 dB (−16.7 to −28.5 LUFS), "my ears burst, then I can barely hear the songs"; peak protection alone left a loud master at full scale and soft stems far under it. Rendered mixes peak near full scale at about −20 LUFS, so the reference lines them up within ±1 dB without a limiter; a quiet mix with sharp peaks comes up only as far as they allow (the desk says so). The level audio's signature holds the levelling (`LevelLoudness.VERSION`, the offset): a level rendered before is levelled the next time its track tab is left |
| **reference** (role, one) | the full performance: what gets transcribed, and what the future demo/auto plays together with the companion |
| **demux** | an action (separate into stems), picked in its card, not a role |
| **start / starts at** | where the audible part of a track starts on the set's timeline (`TimelineStartMs` = `OffsetMs` + `ClipStartMs`). It never goes before 0:00; the trimmed-away start may (so a left-trimmed track can slide left) |
| **volume** | in dB, silence (−60) to **+20 dB** (`ForgeTrack.MAX_GAIN` = ×10); preview amplifies above 0 dB with a gain stage on the track's mixer |
| **Mix the companion** | the view about the final track: it opens hearing the **level audio** (only companion tracks, within its start/end; banner + "hear every track" switch). Solo is hidden there |
| **start and end of a track** | its side trims (`ClipStartMs/ClipEndMs`): drag the sides, or use the bar in its edit panel |
| **mute** | part of the mix: a muted track is left out of the level audio too (saved, undoable) |
| **solo** | only for listening: "hear only this" (click), Ctrl+click adds tracks; not saved. A track is never muted and soloed at once (soloing unmutes it, muting drops its solo); each row says why it is silent (*muted*, *silent: solo*); **clear solo** in the toolbar |
| **edits** | pending, heard live, saved only as a NEW track: **keep this** (all kept parts stay, the rest is muted, timing preserved), **silence this**, **trim to this**, low cut / high cut, pitch (±12 semitones, same speed) |

There is no "music", "song" or "accompaniment" role any more; sets saved
with those names load as companion (`ForgeSetFile` legacy aliases).

## 6b. Media & tracks: import in Resources, arrange in the tracks tab

Two surfaces, one rule set (owner requirements 2026-10-13):

**Resources (setup) is the ONLY place that reads files**: audio tracks
(multiple, via the same navigator pattern as background/video), plus the
stock rows for video and images. With "video provides the audio" on, the
video's audio joins the track list automatically (as an accompaniment).
Nothing else anywhere asks for a path: the **track** tab (a first-class
editor tab, second after setup; fork F-11) only *selects and arranges* the
resources imported or created there.

Roles on each track (owner 2026-10-13): **music** (exactly one: the level's
base track, *connected to the editor's Audio Track*; no second import
surface), **reference** (one: the take transcription uses), **accompaniment**
(many: what the render mixes), **demux** (queue). Role-bearing tracks are
highlighted, and the whole set is shown as a list with each track's waveform
as the row background (never just a count).

- **One place to load tracks, one place for video.** The "Add audio track"
  navigator is the single entry point for audio (pick repeatedly for
  multiple tracks); the stock video row is the single entry point for video
  (with "video provides the audio" on, the video's audio joins the track
  list as an accompaniment; the visuals-only case turns the switch off).
- **One unified track list; every track takes a role there:**
  - **reference** (exactly one, optional): the original take. Transcription
    runs on it automatically (no picker), and the future Mod Auto plays to
    it;
  - **accompaniment** (many): what the beatmap-audio render mixes;
  - **demux** (queue): tracks scheduled for source separation.
  Volume and mute stay per track.
- **Demux results rejoin the list** as new tracks and can take any role,
  but **a demuxed track is never demuxable again** (neither is a demux
  result or a rendered mixdown): "hace perder el tiempo".
- **Track editor v1 (non-destructive, owner 2026-10-13):** editing a track
  **creates a NEW track with a user-chosen name** and adds it to the list;
  the source is never modified. Range operations on a track:
  *keep range only* / *silence range* (both preserve original timing, so an
  edited background stays aligned to the song) and *trim to range* (tight
  file, offset lost). The typical case: a section recording two instruments
  (e.g. alto C + bass C ocarinas) yields a cleaned lead track (→ reference,
  transcribed cleanly) and the other instrument's non-overlapping parts as
  background (→ accompaniment). New tracks inherit the material class of
  their source (a stem stays "separated", a mixdown stays "mixed") and
  start with no roles.
- **Waveform previews & meaningful-audio marking:** one analysis pass per
  track (`kan_forge_analyze_track`) feeds a waveform thumbnail on the row
  (activity is visible *before* selecting anything) and the
  **"no meaningful audio"** rule (both peak and RMS below the noise floor).
  After separation, such stems are labelled ("⚠ seems to contain no
  meaningful audio: select only if you are sure") and **moved to the end of
  the list** so they don't mislead; they stay fully selectable (the user may
  know better).
- **Nothing is mandatory:** demux and transcription are independent,
  optional actions: needed in one case, skippable in another. The render
  only needs its accompaniment selection; a reference is only needed if you
  want transcription (or later the Mod Auto).
- **Render:** the audible accompaniments are mixed
  (`ForgeJobs.RenderMixAsync` → `kan_forge_mix_tracks`: gains applied,
  peak-protected) and installed as the beatmap's audio track; playback
  uses exactly what the editor heard.

Verified live in QA (2026-10-13): track import via navigator, transcription
of the reference, render → `audio-render.wav` set as beatmap audio; role
semantics covered by `TrackRolesTest` / `MixdownRuleTest`.

### 6b.1 Cover credits (owner 2026-10-14)

Setup → **Cover credits**, right under Metadata (`KanCreditsSection`, added
through the ruleset's setup-section list, no fork edit). Levels are often
built on covers people record for the love of it, sometimes together; the
song's artist stays in osu!'s metadata, and this credits the performers:
where the cover was published, each performer's name, what they did and
their links (channel, social networks, support pages), plus an optional
message of thanks. Stored as `kan.credits.json` in the level's files
(`Score/KanCredits`), written when a box is committed or shortly after a
change; an empty form writes nothing. Links are opened only as http(s),
through the game's external-link warning, and each link's button names its
site (YouTube, Instagram, Ko-fi…) so typos show. Showing the credits to
players (song select / results) is a later slice.

### 6c. UI anatomy (presentation layer, 2026-10-13)

Pure presentation over the model above. The shared vocabulary lives in
`forge/Editor/ForgeUi.cs`, so both surfaces read as one program:

- **Role chips** (`RoleChip`): music · reference · accompaniment · demux,
  each with a fixed icon and colour. A chip is filled when its role is active,
  outlined when it is not, and faded with a tooltip when unavailable (demux on
  separated or mixdown material). The long explanations live in tooltips.
- **Selection highlight:** a track with any role gets a frame, a side bar and
  a waveform tint in its primary role's colour (first active role, in role
  order).
- **Waveform strip** (`WaveformPreview`): mirrored bars (sqrt scale), a
  pulsing skeleton while analysing, an optional time axis (the duration is
  read from the file headers and cached by path), the editor playhead, and a
  draggable range bound to the same `EditRange` as the typed seconds boxes.
  It is shaped to become a timeline cell for the coming temporal-segments
  milestone.
- **Resources:** the kan! rows sit in the stock flow, right after the video
  row: *Video provides the audio* (with a live line describing its effect),
  then *Audio Track*, a read-only row that shows the track with the music
  chip and the beatmap file it plays as, then *Audio tracks* (the only audio
  navigator), then *The set* (compact explorer rows). The stock audio chooser
  stays hidden (reflection guard unchanged).
- **track tab** (`KanTracksScreen`): same canvas as the setup tab. Each
  `KanTrackRow` shows, collapsed, the header (name, duration, tags, mute,
  edit, ✕), the waveform strip and the chips. **edit** (or a click on the
  waveform) unfolds the volume slider and the range editor:
  1. range → 2. name → 3. keep / silence / trim.
- **Actions** (`KanActionCard`): one card per optional action. Each card
  says live what it will use, shows its own progress (per track for demux,
  indeterminate for render), and keeps its last outcome. Outcomes are also
  posted to the game's notification overlay, which keeps the history. A card
  is never disabled for a missing selection; running it explains what to
  select instead.
- **✕** asks once (click again within 3 s); removing never deletes files.

### 6d. The set's timeline (owner 2026-10-14)

Every track can be moved in time freely: `ForgeTrack.OffsetMs` (≥ 0) is where
it starts. The set spans `max(start + length)`.

- **Hearing it:** on the track tab, the editor's own play bar plays the set.
  `SetMonitor` points the editor clock at a silent track as long as the span
  (`EditorClock.ChangeSource`, no fork edit). It resizes as tracks move and
  hands the level music back when you leave the tab. Each track plays on its
  own mixer at its start, following volume, mute and **solo** live. Preview
  volume caps at 100%; the render applies the full 0-2 gain.
- **Lanes:** each row's track is a clip on one shared time scale (the span
  plus room to move). Drag the clip to move it (10 ms snap), or type
  *starts at (s)*. Click anywhere to jump there. In edit mode, dragging
  selects a range instead.
- **Render:** `kan_forge_mix_tracks_at` places every input at its offset
  (`kan_forge_mix_tracks` = all at 0). Covered by the native selftest
  (placement at 500 ms, negative offsets refused) and `RenderMixTest`.
- **Edits keep their place:** keep and silence results keep the source's
  start; trim results start at source start + range start
  (`ForgeTrack.OffsetAfterEdit`, `TrackTimelineTest`). Demux stems share
  their source's start.
- **Music on other tabs:** the level's audio file has no offset, so a music
  track that starts later is connected as a copy rendered with leading
  silence. Leaving the track tab re-aligns it if it moved, unless a render is
  now the level's audio.

### 6e. Editing tools of the track tab (owner 2026-10-14)

- **Hear before saving:** each track keeps *pending edits*
  (`ForgeTrack.Edits` / `TrackEdits`): range operations (keep /
  silence / trim, in the track's own time) and a low-cut and high-cut filter.
  Playback applies them live: ranges gate the volume, and the filters are
  `AudioFilter` biquads on the track's mixer. Silenced parts are shaded on the
  waveform. **Save as new track** renders them: keeps and silences through
  `kan_forge_edit_track` first, then one trim to the trims' common span, then
  the filters via BASS (`MediaAudioExtractor.TryFilterToWav`, the same BQF
  as the preview: owner decision to render filters in-game). The source is
  never modified. Its edits are cleared and it is muted, so the mix still
  sounds like the preview, and one undo brings everything back. Tests:
  `TrackEditsTest`.
- **Pitch (owner 2026-10-19):** a piano companion written for another range
  is moved to the instrument's: `TrackEdits.PitchSemitones`, ±12 (an octave
  either way), speed unchanged. Live, the track's channel takes a frequency
  adjustment of 2^(s/12) and a tempo adjustment of its inverse (BASS_FX),
  so its rate stays 1 and it keeps the timeline. Saving renders it after the
  filters with a BASS_FX tempo stream's pitch attribute
  (`MediaAudioExtractor.TryPitchShiftToWav`). The level audio's signature
  includes it, so a pitched companion re-renders the mix. Test:
  `MediaExtractionTest.TestPitchShiftKeepsTheSpeed` (440 Hz → 880 / 220 Hz,
  same length).
- **Undo/redo:** `ForgeSetHistory`, the tab's own history (Ctrl+Z / Ctrl+Y;
  the beatmap's undo is untouched). It covers roles, volume, mute, start,
  pending edits, removals, saves and demux stems. Tracks imported in
  Resources are never removed by an undo.
- **Saving the set with the level:** `ForgeSetFile` (JSON) in the level's
  work folder (`<game data>/kan-forge/<beatmap set id>/set.json`). It is
  saved automatically about a second after any change, plus the toolbar
  **save** button / Ctrl+S, which also saves the level. Every derived file
  (stems, edits, renders, video audio) lives in the same folder instead of
  the system temp directory. The last transcription is saved too, and its
  notes are drawn on the lane of the track it came from. Placing notes *into
  the level* waits on the ruleset's note model (`KanHitObject` is a Phase 0
  placeholder).
- **Lanes:** the compose tab's detailed waveform (`WaveformGraph`, colored
  by low/mid/high content), a shared zoom (Ctrl+wheel, toolbar zoom in /
  zoom out / fit), Shift+wheel to pan, and the view follows the playhead
  while playing.
- **Trim from the sides** (`ForgeTrack.ClipStartMs/ClipEndMs`, file time):
  drag a track's sides in a lane; drag back out to restore. The audio never
  moves, so trimming hides what's outside the window. The file's full extent
  stays faint behind the clip. Playback, the span, the render (clipped tracks
  are trimmed natively, then placed at start + clip start) and **save as new
  track** (the window becomes one more trim) all follow it.
- **Mix the companion** view: a mixing desk (per track: in the song =
  companion role, fader 0-200 %, meter, solo/mute; master: preview the level audio, master meter, render). **Level audio start and end**
  (`ForgeTrackSet.AudioStartMs/AudioEndMs`): handles, start/end at the
  playhead, final length shown. The preview plays only that window, and the
  render trims the mix to it.
- **Picking tracks for actions:** the Demux card has a checklist, the
  Transcribe card a pick-one list (same flags as the chips).
- **Robustness:** demux stems are built and analysed on the worker but only
  added to the set on the update thread. A file that can't be decoded shows
  "can't read" instead of "analysing" forever (`ForgeTrack.AnalysisFailed`),
  and a moved or deleted file shows "file missing".

### 6f. The compose tab: the score editor (owner 2026-10-14)

The **score** is what gameplay judges: each note's timing, pitch (MIDI +
cents), duration, intensity and playing style (normal / legato / staccato
/ accent, vibrato). It is the data that drives the game's feedback loop.

- **Where it lives:** `kan.score.json` inside the level's files
  (`Kan.Core.Score.KanScore`, versioned), so it travels with the level.
  The osu!-format file can't hold pitch or duration (kan! claims legacy
  ruleset ID 0). It is written on save, when leaving the tab and on editor
  exit, and autosaved to the level's work folder; on opening, the newer copy
  wins (`ScoreSession`). Gameplay reads it once the judging core exists.
- **Screen:** `Kan.Forge.Editor.Compose.KanComposeScreen`, shown in place of
  osu!'s ComposeScreen through `Ruleset.CreateComposeEditorScreen()` (fork
  F-13). Tab split: **compose** = the score; **timing** = osu!'s tempo grid,
  which compose snaps to; **design** = feedback/effects, later.
- **Piano roll:** time × pitch over the timing tab's beats and bars. Select /
  draw tools, rubber-band selection, group moves in time and pitch (the
  group keeps its shape), resize by the right edge, snap to the grid with
  Shift for free placement, arrows to nudge, inspector for the selection,
  undo/redo (`ScoreHistory`). Times are on the **level audio's** timeline,
  so a transcription is placed at reference start − level audio start.
- **Listening:** the editor clock plays the level audio (the companion);
  toggles silence the companion and play the reference in sync
  (`ComposeMonitor`, reusing the track tab's channels). The track tab's
  mute and solo apply only in the track tab (owner 2026-10-14): in compose
  the reference toggle alone decides; the track's edits, trims and gain
  still apply.
  **Notes** (`P`, owner 2026-10-14: someone composing by ear may only have
  the accompaniment): the score itself is heard. `Score/ScoreSynth` renders
  each note as a soft tone (fundamental + 2 quiet harmonics, 8 ms attack,
  45 ms release, loudness from intensity, unknown pitches left out, tanh
  soft limit) into a 22.05 kHz WAV in the work folder, off the UI and
  audio threads, 400 ms after the last edit. It plays on its own channel at
  the level-audio time; the previous render is deleted when the new one
  replaces it.
  A channel's first start could drop its seek and play from 0 (the
  reference, the first time it was switched on): `ChannelSync` seeks again
  after starting, and it never takes an offset larger than a buffer as the
  channel's steady offset. A channel that far off is put back in place.
- **Spectrogram:** `Kan.Forge.Media.PitchSpectrogram` (FFT 8192 / hop 1024,
  folded to one bin per semitone, 60 dB range; tested with known tones) per
  source, drawn behind the notes in its role colour (companion blue,
  reference yellow, notes pink), following the toggles.
- **Pitch-range filter:** keep e.g. G4-A8 (`KanScore.KeepRange`); the removed
  notes are listed by pitch to add back.
- **Sections:** set at the playhead or from the selected notes, then
  **record with the mic** (BASS capture via `MicRecorder`, preallocated
  buffer, 1.5 s pre-roll, mic-delay compensation) or **re-transcribe from the
  reference** (the section cut natively, then transcribed). Both replace only
  the notes inside the section (`KanScore.ReplaceSection`, undoable) and
  leave them selected for adjusting.
- **Instrument:** one per level (`ForgeTrackSession.Instrument`, saved in
  set.json), picked in the Transcribe card or the section tools; it selects
  the transcriber's profile family. Until 2026-10-14 the track tab always
  transcribed with the keyboard profile: on an ocarina reference that gave
  258 notes, 161 without a pitch (dropped → "~97"); the ocarina profile gives
  609. The bridge (`profile_for`) also **opens the pitch range** to A0-C8
  for every family (owner: many ocarina sizes; wrong notes are deleted by
  hand): 617 notes. libkan's presets are unchanged.
- **Unknown pitch:** a note heard without a resolved pitch is kept (its
  timing is real), given its nearest neighbour's pitch, drawn grey with "?"
  (`ScoreNote.PitchUnknown`); setting its pitch clears the mark. `U`
  selects them all.
- **Cleaning a transcription** (`KanScore.FromDraft`, measured on the
  ocarina reference 2026-10-14). The transcriber is deterministic (same file
  and instrument → same notes; runs never accumulate). The note counts the
  owner saw grow, ~97 → ~350 → ~650, came from the fixes above: profile,
  unknown pitches kept, open range. Two transcriber artefacts are cleaned
  when a score starts from a transcription:
  - **Open notes:** a note with no release (it runs to the end of the
    audio; 60 of 617) ends where the next note starts. On one-note-at-a-time
    instruments (`IsMonophonic`: ocarina, flute, recorder, wind/bowed)
    every note ends no later than the next onset.
  - **Repeats:** one sound reported twice (`KanScore.FindRepeats`): a note
    starting within 30 ms of another with the same pitch, or of any note on
    a monophonic instrument. Seen: 45 byte-identical twins, 177 pairs in all.
    The copy with a known pitch, then the longer one, stays. On polyphonic
    instruments other pitches (chords) and unknown pitches stay. The
    ocarina reference starts with 468 notes instead of 617. For scores made
    before this: **remove repeats** (toolbar, `Shift+U`), undoable.
  The draft itself stays raw, so the validation export's
  `transcription (automatic).mid` shows what the transcriber really said.
- **Notes follow the tempo grid** (owner 2026-10-22: "timing changes do not
  reach compose"): the score keeps its notes in ms, not as osu! objects, so
  osu!'s "adjust existing objects on timing changes" never moved them. The
  score now remembers the grid its notes were placed on (`KanScore.Grid`,
  saved in `kan.score.json`) and `KanScore.FollowGrid` keeps them on their
  beats when it changes: a moved timing point carries its section's notes
  by as much (the first section reaches back to the start), a new tempo
  keeps each note on its beat counted from the point and scales its length.
  Points added or removed move nothing. Compose checks every frame (the
  timing tab, the tempo bar, osu!'s undo) and honours osu!'s option (off:
  the grid is taken, the notes stay); gameplay's converter follows too, for
  a grid changed without coming back to compose. Not a compose undo step:
  undo snapshots carry their grid and follow the grid of now when they come
  back. A score written before this adopts the grid it first sees. Design
  sections do not follow yet. Test: `KanScoreTest.NotesFollowTheTempoGrid`.
- **A tempo that breathes** (owner 2026-10-22: the reference, a cover
  played without a click, drifts: "the companion changes tempo because it
  feels like it, the solo rushes it… it sounds fine until I want to learn
  it"). Syncing is the level maker's job: kan! proposes no tempo map. The
  tempo bar shows and edits the tempo **at the playhead** ("tempo 3/18";
  BPM, tap and beats per bar change that timing point; the first beat stays
  the first point's). **change** puts a tempo change at the playhead (a copy
  of the one before), **beat here** stretches the tempo before the playhead
  so its nearest beat lands on the playhead (at most ±50 %), **remove**
  takes out the tempo change the playhead is in (the first stays). "Beat
  here" fits the grid to the music, so the notes stay where they sound: the
  grids before and after are joined (`ScoreSession.GridChangeKeepsNotes`),
  and going between them (undo in the timing tab too) moves no note. The
  other tempo edits follow osu!'s option like the timing tab.
  **The beat is ½ / ×2** (owner 2026-10-23: a level timed at 130 BPM in
  quarters, whose pulse is whole notes) makes every beat half or twice as
  long: the same music counted in another note value. Beats per bar and the
  grid's division follow, so bars and lines stay put. A grid whose beats are
  all a power of two of the score's, at the same points, never moves a note
  (`KanScore.FollowGrid`), wherever it comes from: the tool, an undo in
  another tab, a level played before compose saw it. That once squeezed a
  level to half: put to 32.5 BPM, undone to 65 in the design tab, played.
- **Repair the audio's timing** (owner 2026-10-22; section panel). When
  the recording itself drifts, the audio is brought to the grid: **insert
  silence at the playhead** (a part that came in early), **cut the section
  out** (one that came in late, a pause too long), **play the section in the
  length** (it sped up or slowed down: faster or slower, pitch kept),
  **fit the section to the grid** (the nearest whole number of grid steps:
  set the section from a beat the music is on to where the next should
  be). The length (owner 2026-10-22: "1 beat, but also 1/2, 1/4, 1/8,
  1/16, and custom") is a share of a beat of the tempo at the playhead, or
  typed in ms; the shares follow the grid's family ("with triplets, triplet
  lengths": 1/3, 1/6, 1/12; the odd splits their own). **Sync with the next tempo** (owner: "the metronome is
  already fitted in the timing tab: it could tell how much syncs this tempo
  with the next segment"): the next tempo change came in off the beats of
  the tempo at the playhead by a fraction of one; the panel says by how
  much and puts in that silence at the change (early) or cuts it just
  before (late), the smaller (`TimelineEdit.SyncNext`, tested). On the
  owner's level: the change at 10 481 ms, 9.84 beats after the one before,
  took 81.8 ms of silence and landed at 10 562.8, 10 beats after it.
  **The tempo changes after a repair move with the music** (they mark where
  it is: `TimelineRepair.IGrid`; inside a cut they go), so the sections
  after it keep their fit; the step brings them back on undo.
  One `TimelineEdit` (unit-tested: `TimelineEditTest`) applies to the whole
  level (owner: "the changes to the companion must be made to the reference
  too: the level preview and autoplay play it"): every track of the set,
  companion or not, is rendered again where the edit reaches its file
  (`MediaAudioExtractor.TryRetimeToWav`: silence, cut, or BASS_FX tempo;
  4 ms fades at the joins; a track the edit is before only moves), with
  its side trims and pending edits moved along; the notes (a cut removes
  those starting in it), the design's sections and the level audio's end
  follow, and so do the tempo changes after the edit (the grid before it
  stays). The new files are work files; the level audio is
  rendered again. One undo step (`TimelineRepair`): the track set's history
  carries the score and the design (`ForgeSetHistory.IStepState`, file
  paths and waveforms restored), and the score's history links to it
  (`ScoreHistory.UndoLink`), so Ctrl+Z in compose or the track tab undoes
  all of it (compose asks to undo the track tab's later changes first).
  The track tab's and compose's players reopen a track whose file changed.
  Verified end to end in an isolated instance on the owner's level (dev
  hook `KAN_DEV_SCREEN=compose`, `KAN_DEV_REPAIR`): insert / undo / redo /
  cut / stretch moved all three tracks and the notes, the level audio grew
  by the silence and came back on undo; the repaired tracks hold digital
  silence in the gap and match the original shifted (correlation 1.0).
- **Grid:** pick a family (straight / triplets / other), then one of its
  divisions; `G` / `Shift+G` cycle them.
- **Keys:** listed in `ShortcutsPanel` (`K` or the "keys" button); the
  osu editor's own Space/C, X, Z, V and arrow seeks keep working.
- **Try a note by ear** (owner 2026-10-25: placing a note and playing it
  back was clumsy): clicking a key of the keyboard on the left sounds it,
  and sliding along the keys sounds each one reached, as on a piano. The
  score's own tone (`ScoreSynth`), one sample per pitch made the first time
  (`KeyAudition`), in the effects volume; the key flashes.
- **Copy and paste** (owner 2026-10-23: choruses are often alike):
  Ctrl+C / X / V, and the osu editor's Edit menu (`EditorScreen.Copy`).
  The copied notes keep their spacing from the first one; paste puts them
  at the playhead, on the grid when snapping, selected so they move as one
  (one undo step). The clipboard lasts while the game runs, so a phrase
  can go to another level too.
- **Out of range, greyed** (owner 2026-10-23: notes "I cannot use in
  theory"): rows outside the playable range are dimmed under the notes,
  and notes there are grey. The range is the player's own (game settings,
  "My instrument"), else the level instrument's default
  (`RangeFit.Playable`; the ruleset hands the player's range to the forge
  as `RangeFit.PlayerRange`). The note as written is greyed; when the
  level plays, the game may still move the whole level an octave to fit.
  Dev hook steps `copy:from-to`, `paste:at`, `range:low-high`
  (`KAN_DEV_REPAIR`), verified in an isolated instance: 25 notes copied
  from 20-30 s pasted at 60 s, rows outside E5-D6 dimmed.
- **Timing** (`Tracks/ForgeTiming`, kan-forge/timing.json, for gameplay too):
  **mic delay** = the play-along round trip (speakers + player + mic +
  transcriber), the player's, measured before playing a level (2026-10-17:
  moved out of compose to the player loader, `KanMicTimingOverlay`, see
  gameplay.md; 4 count-in + 8 clicks, median lateness of the live
  describer's onsets, `MicCalibration.Estimate`, trusted with ≥ 2/3
  answers and spread ≤ 40 ms); applied to section recordings too; compose
  keeps the slider and a "measure the mic delay" button that opens the
  same `MicTimingOverlay` (the owner: it has a role there too). **Transcriber delay** = the transcriber's lateness on a file;
  applied to every transcription and, when changed, shifts the whole score
  by the difference (undoable).
- **Validation export:** "export MIDI" writes a folder with the reference
  audio, `score (corrected).mid` (ground truth; unknown-pitch notes left
  out), `transcription (automatic).mid` and `about.txt`: both MIDI files in
  the reference file's own time (`Score/MidiExport`, SMF format 0, PPQ 960,
  velocity = intensity; read back by `tools/dataset_bench`'s reader within
  one tick).

### 6f.1 The level's instrument (owner 2026-10-15)

Setup → **Instrument** ("played on"), top of the right column: what the
level is played on: it tells the game how to listen (one note at a time
or several) and the transcriber what to listen for. It is stored in the
level's score file (`KanScore.Instrument`, saved a moment after a change),
so it travels with the level; the track and compose tabs show the same
choice. On opening, a level with a score sets the work folder's choice;
a new score takes the work folder's.

### 6g. The design tab: the level's look (owner 2026-10-15, fork F-15)

The design (`Kan.Core.Design.KanDesign`, file `kan.design.json` in the
level; autosave `kan-forge/<set>/design.json`, the newer copy wins, like
the score) says how the level looks and rewards, stretch by stretch:

- **Sections** cover the level's timeline without gaps: intro, phrase,
  verse, pre-chorus, chorus, bridge, solo, break, outro ("phrase", owner
  2026-10-17, is what the automatic design calls a stretch of music
  between silences; it called them all "verse" before, which read as if
  verses were missing; designs saved before, file version 1, are
  migrated); a name of the author's
  choice; **climax** (hotter colours, half again the sparkle, the
  background glows from below with the music's low end); for rests, what
  the screen shows (the music as light then 3-2-1 / the music as light /
  calm and empty); a palette of their own.
- **The level:** palette (thirteen presets: sakura, ocean, ember,
  forest, aurora, and, owner 2026-10-15, holy (white and gold), space,
  neon, cowboy, cyberpunk, solarpunk, happy, nature; grade colours are
  the same in all, backgrounds stay dark enough for the staff to read,
  unit-tested), sparkle 0-2, glow 0-2,
  section names on/off.
- **Automatic design** (`AutoDesign`, unit-tested): silences of 3.5 s or
  more are breaks (before the first note the intro, after the last the
  outro); the music between them is cut into phrases (kind Phrase) at 1.2 s silences,
  none shorter than 9 s; the busiest, loudest phrases (notes/s × mean
  intensity, top quarter, within 90 % of the best, and only if the best
  stands 15 % above the median) are the chorus/climax. While a design is
  automatic (`Generated`) its sections are generated again whenever it is
  opened or played, so they follow the score; editing a section makes the
  design the author's. A level nobody designed already looks right.
- **The tab:** a timeline of the sections over the notes (click to select
  and seek; drag a boundary to move it), the section's settings, the
  level's settings with a palette preview, and automatic design / split at
  playhead / join with next / undo / redo / save into the level. It is
  saved into the level when leaving the tab or the editor.
- **What is heard** (owner 2026-10-17: "it's difficult to understand the
  song without the notes"): the notes play with the companion by default,
  with the compose tab's toggles (companion (M), reference (R), notes
  (P)) through the same `ComposeMonitor`, so each tab keeps its own state.

The editor loads a level without converting its score into objects
(`KanLevelFiles.EditorLoading`): the osu! format it saves cannot hold a
kan! note (it crashed on opening a level with a score, 2026-10-15).

### 6h. The verify tab: the checklist before a level is shared (owner 2026-10-25, fork F-19)

osu!'s general checks counted the beatmap's hit objects (none in a kan!
level, whose notes live in its score) and reported no drain time and all
the audio unused. The ruleset turns those off (`AppliesGeneralCheck`) and
adds its own (`KanBeatmapVerifier`), reading the score and the mix being
edited, else the level's files. Clicking an issue with a time goes there.

| Check | Type |
|---|---|
| The level has no notes | problem |
| A note outside the usual range of the level's instrument (`RangeFit.DefaultRange`) | warning, per note |
| Two notes at once on an instrument that plays one at a time (`KanScore.SoundingTogether`; overlaps under 20 ms are a legato's seam) | warning, per note |
| A note whose pitch is only a guess (the transcription could not hear it) | warning, per note |
| A note starting after the level audio ends | problem, per note |
| The audio goes on 10 s and 20 % past the last note (osu!'s check, from the score) | warning |
| The level audio is older than the mix, or more than 1.5 dB from its loudness target | warning (while the level is open) |
| No tempo grid | warning |

osu!'s other general checks stay (preview point, background, metadata…).

## 7. URL / YouTube policy (owner direction, 2026-10-13)

The URL fetch path uses **yt-dlp**, which brings cookie handling, ToS and
content-claim exposure. Policy of record:

- **Developer-build tooling only:** the URL field is shown only in
  development builds (or with `KAN_DEV_TOOLS=1`). It stays available for the
  developer build, pragmatic convenience, accepted risk at the maintainer's
  own machine.
- **Not part of the official flow:** the shipped product works on **local
  files** (beatmap audio, user-imported tracks). No yt-dlp dependency, no
  download UX, nothing that can raise platform complaints or claims. (That
  other products get away with embedded YouTube access is luck, not a
  licence; we do not bet the product on it.)
- `kan_forge_has_downloader()` lets the UI reflect this honestly.

## 7b. Self-contained separation (DONE, 2026-10-13; libkan lane)

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
game-side CLI pre-check that stood in until then is removed; the bridge
trusts `find_separator()`'s honest probe.

Validation follow-ups (game lane, 2026-10-13):

- **GPU:** `ggml_backend_init_best()` picks the GPU automatically, but the
  backend must be *built*: the game's bridge build enables `GGML_VULKAN`
  (with a CPU fallback when Vulkan tools are absent; `BSR_FORCE_CPU` is the
  runtime escape hatch). **Note for the libkan lane:** the monorepo root
  build still compiles ggml CPU-only; defaulting `GGML_VULKAN` ON there
  (when Vulkan is available) would give every consumer the GPU path.
- **Progress:** the runtime forwards per-chunk fractions correctly; the
  game side now passes its progress sink into the separation job (it was
  silently dropped before, freezing the bar at 0%).


### §7b.2 Performance of the parity gate (needs from libkan, 2026-10-14)

The gate is correct and stays ("a broken GPU path cannot ship stems", the
owner demand). What a user experiences on the first run after a runtime
change is a **silent, CPU-heavy one-time probe**, which reads like a
GPU→CPU regression even though measured runs are 2 s on GPU (RX 6800,
parity 60.1 dB). The game side now reports the gate as its own progress
phase with its verdict ("device check (GPU ok)" / "… (CPU fallback)").
Two perf asks for the libkan lane:

1. **Marker keying** (**done 2026-10-16**): the marker is keyed on the
   ggml and BSRoformer pins plus the backend tag (`ok v4 vk ggml <sha> bsr
   <sha>`), so only a pin update re-probes. Before, it embedded
   `__DATE__ __TIME__` of `roformer_runtime.cpp`, so every recompile
   re-ran the probe: minutes of CPU before each first demux, read by the
   owner as "the GPU limit made it slower" (no GPU limit was ever shipped;
   the 2026-10-15 throttling experiments were reverted).
2. **Probe cost & visibility:** the CPU reference pass on q8_0 is the slow
   half. A shorter probe clip (or subsampled SNR) would shrink it; a
   progress callback during the probe would make it honest from inside.

### §7b.3 Transcriber artefacts (needs from libkan, 2026-10-14)

**Status 2026-10-15:** items 1-2 are fixed in libkan (`main` 2b114bc:
one record at a time on mono profiles). The slur, tonguing and breath
articulation (3e6ca04) raises the ocarina F1 from 37.6 % to 57.0 %. Both
are merged into agent/game; see [transcription-quality.md](transcription-quality.md).
The game-side cleanups (`FindRepeats`, `endOpenNotes`) stay: drafts made
before the fix still carry the artefacts, and on new ones they find
nothing to do.

Measured on an ocarina reference with `transcribe_audio` through the bridge
(open range A0-C8, deterministic over runs). The game cleans both up (§6f),
but the source is in libkan:

1. **Duplicate notes:** the ocarina profile reports one sound under two
   note ids: 45 byte-identical twins (same onset, end, MIDI, cents,
   dynamics) and more same-pitch pairs within 30 ms (177 pairs in all of
   617 notes). The keyboard profile on the same file gives none. The cause
   is probably two Hit events for one onset in `NoteDescriber`'s monophonic
   path.
2. **Unterminated notes:** 60 notes are closed only by `flush()`, with
   the end of the file as their end (minutes long, `end_faded` not set). A
   note should close at the next onset (monophonic), or be flagged
   `end_faded` when only the flush ends it.
3. **Expected (candidate) mode, measured 2026-10-15** with
   `kan_forge_bench` (details: [transcription-quality.md](transcription-quality.md)).
   Per note it confirms the right pitch better than blind mode (72.7 % vs
   65.5 %) and rejects ±1-2 semitones. But:
   - legato changes don't consume the pending expectation;
   - a pending expectation can't be replaced;
   - monophonic deferral creates the record without its expectation
     (only 7.8 % bound);
   - an expectation one octave below the played note is accepted 74 % of
     the time.

   Streamed as a game uses it, only 10.6 % of notes are confirmed, so
   gameplay stays blind until these are addressed.

### §7b.1 Device correctness guard (2026-10-14)

A ggml-vulkan q8_0 bug (wrong integer dot-product matmul on RADV NAVI21)
produced silently wrong stems. Hardened so this can never lock in:

- int-dot path disabled by default before ggml init (override respected);
- **one-time CPU/GPU parity gate** (>= 30 dB SNR on content stems) with
  automatic CPU fallback: a broken GPU path cannot ship stems;
- pinned ggml/BSRoformer commits (update only with the parity test green
  on real hardware);
- upstream report: docs/porting/ggml-vulkan-int-dot-product.md.

## 8. Remaining slices (mapped to the prompt's acceptance criteria)

| Slice | Covers | Notes |
|---|---|---|
| 2. NLE timeline UI | criteria 2-3: up to 6 stem tracks + source + video track laid out in time; cut/move/delete on any track; video trim (YouTube intros/outros/ads); edits propagate | the track set (volume/mute/import) and the propagation engine are done; this is the timeline UI + a time-based track model; per-track volume/mute UI moves from the section into the track headers |
| 3. Transcription editor UI + persistence | criterion 4: per-note pitch/timing/duration/technique editing, comparison metadata inspection ("unjudged" states), persisting the draft into the level store | `KanDraftNote` already models all fields |
| 4. Environment polish | criterion 5: GPU/CPU auto device + model auto-download flows verified through the UI | mostly verification; the bridge already reports download progress |
