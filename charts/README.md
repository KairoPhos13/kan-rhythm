# charts/

Song and chart data for kan-rhythm: the chart format specification, official
charts, and validation tooling references.

**Status:** planned — this folder is a placeholder.

## Intended contents

- `spec/` — the chart format specification (notes, timing, difficulty tiers,
  pitch targets in cents, metadata)
- `official/` — bundled official charts, one folder per song
- Charts are data, not code: prefer a text-based, diff-friendly format (JSON/TOML)
  so they can be reviewed in pull requests

## Ground rules

- English only (see root `CONTRIBUTING.md`); song titles/lyrics metadata may
  keep their original language in dedicated fields
- Every chart must pass the validator in `tools/` before merging
- Extractable into its own repository later (e.g. a community chart pack repo)
