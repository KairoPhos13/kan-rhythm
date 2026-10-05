# charts/ — TODO

Song/chart data and the **`.kan`** / **`.kchart`** format specification.
Spec reference: [docs/SPECIFICATION.md](../docs/SPECIFICATION.md) §5.

## Format specification (`spec/`)

- [ ] ADR: container format (renamed .zip, osu!-`.osz` style) and why
- [ ] `manifest.json` schema: metadata, author, BPM timing points,
      SHA-256 integrity signature (for leaderboards)
- [ ] Layer 1 definition: `audio_backing.ogg` + `audio_target_stem.ogg`
      (Opus/Vorbis @ 48 kHz), variable-tempo timing map
- [ ] Layer 2 `canonical_score.bin` schema: time events in microseconds,
      MIDI note / Hz vectors, duration, articulations (staccato, vibrato,
      slide, chord)
- [ ] Layer 3 `.kchart` JSON schema: per-instrument arrangement
      (fret/string, transposition, simplifications), difficulty tiers
- [ ] Instrument profile metadata: family, attack type (plucked/wind/bowed/
      keyboard), range, blanking window defaults
- [ ] Instrument pack spec (platform-architecture.md §5): profile + technique
      vocabulary (bend, vibrato, slide, harmonics, mute...) mapped onto
      canonical-score articulations + fingering-hint data fields in Layer 3
      (`fret`, `string`, `finger`, `bow_direction`) + helper renderer config
- [ ] Versioning policy: adding an arrangement must not require re-uploading
      audio (gate: < 50 KB per new instrument arrangement)
- [ ] Publish the spec so the community can build external tools
      (future `kan-spec` public repo)

## Reference data (`official/`)

- [ ] 1 demo song fully authored (public-domain/CC recording) with
      guitar + flute + piano arrangements — exercises all 3 layers
- [ ] Golden test fixtures: `.kan` files used by tools/ validator tests

## Rules

- Charts are data, not code: text-based, diff-friendly formats only
- English metadata fields; original-language titles/lyrics in dedicated fields
- Every chart must pass the tools/ validator before merging
