# tools/: TODO

CLI utilities supporting development and content creation.

## Chart tooling

- [ ] `kanchart-validate`: schema/timing/pitch validator for `.kan` packages
      and `.kchart` files (used as CI gate for charts/)
- [ ] `kanchart-info`: dump manifest, layers, difficulties of a package
- [ ] `kanchart-pack` / `kanchart-unpack`: author-friendly (un)packing of
      `.kan` containers with SHA-256 manifest regeneration
- [ ] `kanchart-convert`: bidirectional `.kan` ↔ `.osz` (osu! beatmap)
      conversion (ADR-0010, osu-integration.md §3): Layers 1-3 → audio +
      control points + ruleset HitObjects + difficulties; round-trip
      lossless for kan! data; osu!-only extras kept on the `.osz` side

## Audio analysis

- [ ] `kananalyze`: offline onset/pitch extraction over a WAV (libkan in
      `--loopback` mode) to assist manual chart authoring
- [ ] Spectrogram renderer to PNG for docs/review of reference songs

## Calibration

- [ ] `kancalibrate`: measure and report per-device input/audio latency
      offset; produce calibration profiles consumed by the game client
- [ ] Loopback latency harness support (Milestone 1 gate: ≤ 4.5 ms onset)

## kan!check core (biomechanical linter)

- [ ] Rule engine library shared with the kan!forge editor:
  - [ ] String: impossible fret/string jumps given BPM (e.g. fret 1 → 15
        in 50 ms @ 160 BPM = red alert)
  - [ ] Wind: missing breathing windows over long continuous passages
  - [ ] Keyboard: hand-span violations, impossible polyphony
- [ ] Machine-readable output (JSON) so the editor and CLI share results

## Research (Milestone 4)

- [ ] Anti-cheat signal classifier prototype: detect injected synthetic
      MIDI/synth audio posing as acoustic mic input via phase
      micro-fluctuations and organic noise analysis (gate: ≥ 99% block)

## Rules

- One executable per tool, own subfolder + CMakeLists, wired into superbuild
- May depend on libkan/; libkan/ never depends on tools
- English only (CONTRIBUTING.md)
