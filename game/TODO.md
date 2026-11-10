# game/: TODO

The **kan!** client on osu!lazer (ADR-0010) + integrated **kan!forge** editor.
Integration architecture: [docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).
Gates: [docs/ROADMAP.md](../docs/ROADMAP.md).

## First public release (owner 2026-10-27)

The goal is modest: publish kan! as it is, free, for whoever wants to use
it. It is already useful and fun; it may grow later, by its owner or by a
community that finds it valuable. Income and who maintains it are
secondary. What it is for: making practice fun, so players take what they
learn to their churches, friends and communities.

**Design stance:** kan! does not chase anxiety. osu!'s higher difficulties
are frantic and built to be compulsive; a song may be intense, but the game
itself never aims at pressure or compulsion. Forgiving defaults, calm
feedback, strictness only as an opt-in.

Blocking (legal and safety):

- [x] Own identity, provisional (2026-11-09, fork F-22): kan!'s logo
      (owner's design: white ring fading to gold, "kan!" white with silver
      borders), app icons, triangles intro and the eight menu backgrounds,
      all generated from scratch by `tools/gen-provisional-brand.py` into
      `game/brand/` (F-21); osu!'s voice and theme music are never played.
      Left, not blocking: kan!'s own intro music (owner, undecided); the
      "welcome" and "circles" intros still use osu!'s art if a player picks
      them in settings; osu!'s UI sounds and fonts stay (CC BY-NC 4.0 /
      their own licences, used non-commercially, credited)
- [x] Online off without removing it (fork F-20, 2026-10-27):
      `KanEndpointConfiguration` has no server; menu banner, supporter note
      and the ranked-metadata download stay off; Discord presence off.
      kan!'s own osu-web later is filling in that file
- [x] Updater reads github.com/KairoPhos13/kan's releases (F-20)
- [x] Windows file and link associations not installed: they were osu!'s
      and would take them from an installed osu! (F-20). kan!'s own file
      type and link scheme: later
- [ ] User-facing "osu!" strings: English first pass done
      (2026-10-27); left: other languages' translations, osu!-only
      features still reachable (osu!stable import, osu! tutorial button),
      list in `docs/fork-divergence.md`
- [ ] Bundled songs free to distribute (public domain, Creative Commons or
      original): none popular. Players share their own levels as files
- [ ] Separation model (owner decision): not shipped, downloaded on first
      use from Hugging Face. The default (q8,
      `FractalSurfer/BS-RoFormer-SW-6stem`) declares no licence; the f16
      (`studio409/BSRoformer-SW-6Stem-GGUF`) declares MIT, as a mirror of an
      MIT ONNX release. Options: f16 as default, host our own copy
      (`KAN_MODEL_URL`), or keep and say so. Basic Pitch (Apache-2.0) to be
      listed in the notices
- [ ] Push the fork commits to github.com/KairoPhos13/kan (owner: this build
      machine has no credentials). The submodule must resolve for others
- [ ] No donations until BASS is settled (`docs/bass-licensing.md`): a free,
      non-commercial release needs no licence

Important for a first impression:

- [ ] Tried on more instruments (a recorder or flute, voice, a bowed and a
      plucked one) and on other microphones (laptop, headset, Windows'
      noise suppression)
- [ ] Windows build and installer verified; macOS if possible
- [ ] First run: choose the instrument, test the microphone, calibrate,
      play an easy level
- [x] osu!'s own game modes are not shipped (fork F-9, 2026-10-13)
- [ ] A short demo video; the root README for players (game/README.md
      is done, 2026-10-27)

Later, if it grows: kan!'s own server from osu!'s open ones (osu-web,
osu-server-spectator), shared levels with terms of use and a takedown
process, rankings. Cheating is accepted as out of scope (owner: whoever
cares about it can fix it); osu!lazer's model (ranked scores only from
official builds, replays kept) is the reference.

## Milestone 2: Fork + ruleset vertical slice

### Setup & compliance (do first)

- [x] Fork `ppy/osu` (osu!lazer, MIT) → **`kan`** (github.com/KairoPhos13/kan);
      submodule wired at `game/client/` (2026-10-12)
- [x] Baseline build green on **Linux** (.NET 10 SDK: the fork targets
      `net10.0`, "dotnet 8" plans are superseded) + `game/docs/environment.md`
      (toolchain, commands, timing notes), verified 2026-10-12: build
      0 warnings / 0 errors, running **kan!** window, ruleset plugin loading
      (log-proven)
- [ ] Verify baseline build on Windows/macOS (same commands; unverified;
      record results in `game/docs/environment.md`)
- [ ] **Push fork commits to the fork repo** (`0f5db37`, `2970c68` →
      github.com/KairoPhos13/kan): blocked on this build machine having no
      GitHub credentials. The maintainer must push before merging
      `agent/game` into `main` (see `game/docs/fork-divergence.md` pin log)
- [x] Rebrand baseline (ADR-0011, visible from the first build): window
      title + splash text say **kan!**; about screen (Settings → General →
      About kan!) credits "Built on osu!lazer (MIT), ppy/osu contributors";
      `THIRD_PARTY_NOTICES` generated (`game/tools/gen-third-party-notices.py`)
      + embedded + shown in about screen; modified-file marking policy in
      `game/docs/fork-divergence.md` (2026-10-12; interactive screenshot QA
      of the about screen still pending)
- [ ] String sweep: user-facing `osu!` references → `kan!` in adapted code
      (scope list in `game/docs/fork-divergence.md`; mechanical deltas logged
      there)
- [ ] Identity design v0: logo/wordmark, color palette, typography, default
      skin shell (original or license-clear assets per `assets/` rules),
      includes replacing the osu!-derived intro visuals and `lazer.ico` app
      icon (trademark hygiene, ADR-0011 §5)
- [x] BASS licensing resolution: analysis + recommendation in
      `game/docs/bass-licensing.md` (2026-10-12: verify applicability with
      un4seen first; budget one-time commercial licence if `kan!supporter`
      ships paid perks; backend replacement not needed; capture path is
      miniaudio already). Closing action: written answer from un4seen on file
- [x] Create `game/docs/fork-divergence.md` (every fork-level change logs:
      what/why/merge-conflict cost/upstream-PR status), started 2026-10-12
- [x] Empty kan ruleset plugin loading inside the fork (proves plugin path,
      `Kan.*` namespaces for new code): `game/ruleset/` (Kan.Ruleset),
      assembly `osu.Game.Rulesets.Kan` (loader-glob constraint, see
      fork-divergence.md F-8); placeholder mode screen; log line
      "kan! ruleset loaded" proves discovery (2026-10-12)
- [ ] QA pass: screenshot-verify about screen + play the placeholder mode on
      a real beatmap (2026-10-12 fork test suite status:
      `osu.Game.Tests` ran headless: 5481 passed / 9 failed / 23 skipped in
      16m20s; 2 of the 9 are flaky (spectator `TestClientSendsCorrectRuleset`,
      editor `TestRestart`; pass on rerun), 7 fail deterministically in code
      the Phase 0 deltas do not touch: 4× chat-link action mapping, beatmap
      importer case-folding, star-display migration)
- [ ] Verify those 7 deterministic failures reproduce on a pristine
      `816df139fd` build (upstream/environment issue, not ours) and, if so,
      consider reporting upstream

### Instrument modes & selection (owner direction 2026-10-12, see
`game/docs/instrument-modes.md`)

- [x] Remove stock game modes (osu!standard/taiko/catch/mania) from the
      client build + osu!stable legacy IPC; instruments own the mode slots
      (fork deltas F-9/F-10, 2026-10-13; build green, run verified)
- [ ] Instrument taxonomy v0 (owner scope: **piano** = universal "Keys"
      profile, **ocarina** = "winds/others"; later bass, guitar, drums).
      Resolve the open questions in `game/docs/instrument-modes.md` §6 when
      the owner describes the next work item
- [ ] Instrument-as-ruleset architecture (recommended: shared
      `Kan.Ruleset.Core` + thin per-instrument facades, one per
      `osu.Game.Rulesets.*.dll` assembly per the loader constraint), needs
      owner sign-off before implementation
- [x] Instrument selection v0 (owner 2026-10-13): **category-tied selector**
      (Category → Instrument dropdowns, `KanInstrumentCatalog`) + **local
      persistence** via ruleset config (`client.realm`, remembered across
      runs). Lives in Settings → Rulesets → kan! (plugin surface, zero fork
      divergence), 2026-10-13
- [ ] Instrument selection at play start (song select / play setup
      placement) + variant selector (ocarina types) + wiring the selection
      to instrument-wise effects (kanfx packs) once the packs exist
- [ ] Instrument pack schema v0: piano (universal) + ocarina variants
      (range/transposition profiles) per platform-architecture §5
- [ ] Personalization menu for the instrument list (deferred by owner:
      many instruments per category)

### Input & judgment core

- [ ] C-ABI shim in `libkan/abi/`: fixed-size POD event queue (onset, pitch
      sample, technique findings), atomic parameter handles, no per-frame
      allocations across the boundary
- [x] Live description through the bridge (`kan_forge_live_*`, same
      describer as offline; selftest: live == offline onsets) and a C#
      session on a worker thread (`Kan.Forge.Play.LiveSession`), 2026-10-14
- [ ] Measure end-to-end input→event latency (must preserve M1 gates)
- [x] MVP judgment (`Kan.Forge.Play.LiveJudge`): timing + the right note,
      waits for the pitch; no game over, see docs/gameplay.md
- [ ] Sustain, articulation and intonation scoring (shown, not judged yet)
- [ ] Dual-axis judgment on top of osu!'s judgment infrastructure:
      instant attack (300/GREAT/MISS) + delayed pitch validation visual
      (~+30 ms: keep glowing vs fail morph)
- [ ] Consume libkan events via the C-ABI queue only; never call DSP from
      the update/draw threads

### Game feel & calibration

- [ ] Single-lane highway using ruleset drawables @ 120+ FPS
      (gate: frame jitter < 2 ms; largely inherited, verify with our skin)
- [ ] Judgment indicators (dual-axis: attack timing + intonation): kan!
      visual vocabulary, not osu! hit bursts (ADR-0011)
- [ ] HUD redesign: intonation meters, sustain lanes, instrument helpers.
      Keep osu!'s score/combo/accuracy presentation grammar underneath
- [ ] 3-step calibration screen (acoustic latency, noise floor, fixed-
      instrument detune → Δc applied to kanfx backing-track pitch shift)
- [ ] Instrument helper renderers (per instrument pack): fret/string bubbles,
      fingerings, intonation sustain lanes (all toggleable beginner→pro)
- [ ] Skin support: kan! default skin + osu! skin compatibility where
      concepts map cleanly
- [ ] Mods: reuse osu! `Mod` classes; add kan-specific mods (NoHelpers,
      intonation strictness) with ranked-eligibility flags
- [ ] Support 3 test instruments end-to-end: acoustic guitar (mic),
      recorder flute, keyboard/piano
- [ ] Ambient-noise false-trigger test (gate: < 1% @ 45 dB SPL, 50 cm)
- [ ] Auto-pitch backing-track gate: −35-cent detuned instrument matched
      in < 2 s calibration, no artifacts, no score loss

### Content pipeline

- [ ] `.kan` ↔ `.osz` converter in `tools/` (Layer 1/2/3 mapping per
      osu-integration.md §3); round-trip lossless for kan! data
- [ ] Import one converted demo song (charts/official/) as beatmap
      difficulties and play it end-to-end

## Milestone 3: kan!forge editor (implementation notes:
`game/docs/editor-pipeline.md`)

- [x] Entry-window pipeline (prompt §2.1): "Separate audio stems" +
      "Generate transcription" with live progress bars driven by the libkan
      progress API, background jobs while the form stays usable:
      `KanCreationSection` in the editor setup screen (2026-10-13)
- [x] libkan C-ABI bridge (`game/forge/native`): POD notes, progress
      contract, fetch/separate/transcribe, environment probes + native
      self-test with synthesized input (2026-10-13)
- [x] Transcription draft model + temporal-edit propagation (cut/move/delete
      → shift/split/remove, physically faithful, tested, the §2.2 hard
      requirement) (2026-10-13)
- [ ] Editor authors per instrument (shared taxonomy with the client per
      `game/docs/instrument-modes.md` §5)
- [x] **Unified track list with roles** (owner 2026-10-13): one load place
      for audio + one for video (video audio joins the list on "video
      provides the audio"); every track takes a role: **reference** (one,
      optional; transcription runs on it automatically; future Mod Auto
      plays to it), **accompaniment** (the render mixes them), **demux**
      (queue). Demux results rejoin the list and are **never demuxable
      again** (as aren't demuxed sources or mixdowns). **Nothing is
      mandatory**: demux and transcription are independent optional steps.
      Volume/mute per track; render → beatmap audio. Merged into the stock
      Resources section (`KanMediaSection : ResourcesSection`). Verified in
      QA + `TrackRolesTest`/`MixdownRuleTest` (2026-10-13)
- [x] **Track editor v1** (owner 2026-10-13): non-destructive range edits
      create **new tracks with user-defined names** (keep range / silence
      range / trim to range; timing preserved where it matters for
      background alignment), covers the two-instruments-in-one-take case
      (clean lead → reference; the other instrument → background).
      Native `kan_forge_edit_track` + row UI + self-test (2026-10-13)
- [x] **Waveform previews + "no meaningful audio" marking** (owner
      2026-10-13): per-row waveform thumbnails (`kan_forge_analyze_track`);
      after separation, empty-looking stems are labelled and **moved to the
      end of the list** (still selectable) (2026-10-13)
- [ ] **Needs from libkan (owner 2026-10-13): self-contained separation**.
      The official flow must not require an installed separator: compile/
      provision the GGUF RoFormer runtime (GGML, single stack) into libkan
      so `kan::separate_stems` works with only the auto-downloaded model;
      also `find_separator()` should probe the runtime (not just the model)
      and failed runs must not report 100% progress. Spec:
      `game/docs/editor-pipeline.md` §7b. Until then demux is developer
      tooling (game-side pre-check is provisional)
- [x] **"tracks" editor tab** (owner 2026-10-13): the track editor is a
      first-class editor tab next to setup/compose/design/timing/verify
      (fork F-11 generic hook `Ruleset.CreateTracksEditorScreen()`); the
      tab holds the unified list + roles + track editing + actions, and
      **Resources is the only file-reading surface** (multiple audio
      imports; video/images via the stock rows; video audio joins the list
      automatically). Verified live (2026-10-13)
- [x] URL/YouTube policy (owner 2026-10-13): yt-dlp fetch is **developer-
      build tooling only** (hidden outside dev builds / `KAN_DEV_TOOLS=1`);
      the official flow is local files only, no claims exposure
- [ ] NLE timeline UI (prompt §2.2): up to 6 stem tracks + source audio +      a video track (extend background visuals to VIDEO sources); volume/mute
      per channel; cut/move/delete on any track; the video trim use case
      (cutting intros/outros/ads from a YouTube recording); every edit drives
      the propagation engine
- [ ] Transcription editor UI (prompt §2.3): per-note pitch/onset/duration +
      EXPRession facts (vibrato, bend, dynamics tier) editing; comparison
      metadata inspection (onset anchor, bias-corrected duration + raw end +
      `end_faded`, `saturated`, presence/sim) with "unjudged" states; edited
      fragments shown as proposals
- [ ] Persist the draft into the level store (backend-data-model.md) +
      `.score` / MIDI / JSONL import/export (libkan interchange formats)
- [ ] In-editor per-track **playback** toggles (the render-side volume/mute
      exists; the timeline needs audition/playback per channel,
      chart-transcription-evaluation.md §3d)
- [ ] Environment polish (acceptance criterion 5): GPU/CPU automatic device
      + model auto-download UX; first-class error surfaces (missing model,
      missing bs_roformer, unsupported codec, zero resolved notes)
- [ ] Interactive spectrogram timeline @ 60 FPS (editor overlay on osu!
      editor framework where possible)
- [ ] Inference stack per chart-transcription-evaluation.md (**GGUF +
      Vulkan, single stack**): bs_roformer source separation + transcription;
      heavy AI dependencies remain optional layers. Gates: 3-min song → stems
      < 25 s (GTX 1650/M1), ≥ 80% pre-placement draft accuracy
- [ ] Drag-and-drop MP3 → stems → draft notes workflow
- [ ] Importers: `.gp5`, `.musicxml`, `.mid` → Layer 2 canonical score
      (consume `chart_scribe` MIDI drafts directly)
- [ ] Chart authoring UI for Layer 3 (`.kchart`) incl. fingering fields
- [ ] `kan!check` biomechanical linter integration (red alerts: impossible
      fret jumps at BPM, missing breathing windows for winds)
- [ ] Mapping-time gate: 3-min song → playable synced level for 2 instruments
      in < 20 min

## Milestone 4: Community client features

- [ ] In-client map browser (1-click `.kan` download): osu! browser
      patterns adapted to our `server/` OpenAPI
- [ ] Lightweight replay system (micro-timing + cents curves)
- [ ] `kp` display, per-instrument/per-input-method leaderboards
- [ ] `kan!supporter` cosmetics, medals, fast-download hooks (donations/)

## Rules

- English only; minimize fork divergence (log every fork delta);
  license compliance ship-blocking; depends on libkan/kanfx C-ABI only;
  never on their internals from C#.
