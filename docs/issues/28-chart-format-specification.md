# Publish the `.kan` / `.kchart` format specification

Labels: `docs`
Size: M

## Context

The 3-layer beatmap format is core architecture (root
[README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md)):
Layer 1 (audio + timing), Layer 2 (canonical score), Layer 3
(`.kchart` per-instrument arrangements, < 50 KB so new instruments never
require re-uploading audio). Today the format lives as prose in
[docs/SPECIFICATION.md §5](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/SPECIFICATION.md)
and as open items in
[charts/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/charts/TODO.md)
— there is no spec a community tool author could build against.

Publishing the spec is what turns "community-driven" into practice:
external tools, converters and level-sharing sites can only grow if the
format is documented and versioned. ADR-0006 (the 3-layer format) is
marked "ratify when charts/spec/ lands" — the spec is the trigger.

## What to do

1. Write the spec under `charts/spec/` (text, diff-friendly, English),
   covering the items listed in
   [charts/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/charts/TODO.md):
   - **Container**: `.kan` as a renamed zip (`.osz`-style) — with the
     ADR explaining why;
   - **`manifest.json` schema**: metadata, author, BPM timing points,
     SHA-256 integrity signature (leaderboards will depend on it);
   - **Layer 1**: `audio_backing.ogg` + `audio_target_stem.ogg`
     (Opus/Vorbis @ 48 kHz), variable-tempo timing map;
   - **Layer 2** `canonical_score.bin` schema: microsecond events, MIDI
     note / Hz vectors, duration, articulations (staccato, vibrato,
     slide, chord);
   - **Layer 3** `.kchart` JSON schema: per-instrument arrangement
     (fret/string, transposition, simplifications), difficulty tiers,
     fingering-hint fields (`fret`, `string`, `finger`,
     `bow_direction`);
   - **Instrument profile metadata** (family, attack type, range,
     blanking defaults) and the instrument-pack spec referenced by
     [docs/architecture/platform-architecture.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/platform-architecture.md) §5;
   - **Versioning policy**: adding an arrangement must never require
     re-uploading audio (< 50 KB budget per arrangement).
2. Include at least one worked example (a minimal valid `.kan` package
   listing + one `.kchart`), and a "how to read this spec" section for
   external tool authors.
3. Where the in-game editor already writes real files, cross-check the
   spec against what it produces (`kan.score.json`, `kan.design.json`
   and rendered audio — see
   [game/docs/editor-pipeline.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/editor-pipeline.md) §6)
   and note any divergence as follow-up issues rather than papering over
   them.

## Acceptance criteria

- [ ] `charts/spec/` exists with container, manifest, Layer 1/2/3,
      instrument-pack and versioning sections
- [ ] At least one worked example package + one `.kchart` included
- [ ] Cross-check against the current editor's real file outputs done;
      divergences filed as issues
- [ ] ADR-0006 ratified or updated to point at the spec (maintainer
      merges the ADR)
- [ ] The spec is linked from `charts/README.md`, `docs/SPECIFICATION.md`
      and the root README's architecture section

## Pointers

- [charts/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/charts/TODO.md) —
  the spec's itemised scope
- [docs/SPECIFICATION.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/SPECIFICATION.md) §5 —
  current format prose
- [docs/architecture/platform-architecture.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/platform-architecture.md) §5 —
  instrument packs and helper-renderer config
- [game/docs/editor-pipeline.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/editor-pipeline.md) §6 —
  what the editor actually writes today
- [docs/adr/](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/adr) —
  ADR-0006 (3-layer format) and the ADR rules (immutable once accepted)

## Notes

- Charts are data, not code: text-based, diff-friendly formats only
  ([charts/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/charts/README.md)).
- English metadata fields; original-language titles/lyrics in dedicated
  fields — keep that rule in the schema.
