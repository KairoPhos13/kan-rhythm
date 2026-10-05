# game/ — TODO

The **kan!** client and the integrated **kan!forge** editor.
Milestones 2–3. Gates: [docs/ROADMAP.md](../docs/ROADMAP.md).

## Milestone 2 — Vertical slice (game feel)

- [ ] Engine decision + ADR: Godot 4 (GDExtension C++) vs own SDL3/Vulkan client
- [ ] Single-lane note highway renderer @ 120+ FPS
      (gate: frame time jitter < 2 ms)
- [ ] Consume libkan events through the lock-free ring buffer
      (never call the DSP from the render thread)
- [ ] Instant hit feedback on onset (<15 ms to pixels): sparks, impact SFX,
      300/GREAT/MISS judgment
- [ ] Delayed pitch validation visual (~+30 ms): hit keeps glowing if correct,
      morphs into fail (broken string / detuned sound) if wrong
- [ ] Dual judgment scoring: attack timing + intonation stability
      (sustain lanes like osu! sliders)
- [ ] 3-step calibration screen: acoustic latency, noise floor,
      fixed-instrument detune (gate: −35-cent detuned instrument matched
      in < 2 s, no audible artifacts)
- [ ] Dynamic backing-track mix: attenuate target stem on combo (−12 dB),
      abrupt energy loss on miss (Mute on Miss)
- [ ] Support 3 test instruments end-to-end: acoustic guitar (mic),
      recorder flute, keyboard/piano
- [ ] Ambient-noise false-trigger testing (gate: < 1% @ 45 dB SPL, 50 cm)

## Milestone 3 — kan!forge editor

- [ ] Interactive spectrogram timeline @ 60 FPS (notes visible as bright lines)
- [ ] ONNX Runtime integration (local, async — never during gameplay)
  - [ ] HTDemucs-6s stem separation (gate: 3-min song < 25 s GTX 1650/M1,
        < 90 s CPU)
  - [ ] Basic Pitch draft transcription (gate: ≥ 80% notes correct pre-human)
- [ ] Drag-and-drop MP3 → stems → 80% draft notes workflow
- [ ] Importers: `.gp5`, `.musicxml`, `.mid` → Layer 2 canonical score
- [ ] Chart authoring UI for Layer 3 (`.kchart`): fret/string assignment,
      octave transposition, beginner simplification
- [ ] `kan!check` biomechanical linter integration (red alerts: impossible
      fret jumps at BPM, missing breathing windows for winds)
- [ ] Mapping-time gate validation: 3-min song → playable synced level for
      2 instruments in < 20 min

## Milestone 4 — Community client features

- [ ] In-client map browser with 1-click `.kan` download
- [ ] Lightweight replay system (micro-timing + cents curves only)
- [ ] `kp` (kan! points) display, per-instrument/per-input-method leaderboards
- [ ] `kan!supporter` cosmetics, medals, fast-download hooks (see donations/)

## Rules

- English only; real-time safety (CONTRIBUTING.md); depends on libkan/,
  never the reverse; keep extractable into its own repo.
