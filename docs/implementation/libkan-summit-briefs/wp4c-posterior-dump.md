# Work package 4c: dump the learned posteriors for study

You are the builder. Read `docs/implementation/libkan-summit-engineering.md`
and, in `game/forge/native/`, `kan_truth_bench.cpp`, `truth_bench.hpp`,
`truth_paths_bp.hpp`. Work only in `game/forge/native/`. Do not change
`libkan/`, `tools/level_pack`, `charts/`.

## Deliverable

A new option of `kan_truth_bench`: `--dump-posteriors <dir>`. With it,
the `bp` path writes, for every excerpt and every case it runs, the
whole-recording posteriors it already computes and the score it was
given. Nothing else changes: results and existing outputs stay byte
identical with and without the option.

Files, named `<e>_<case>` where `<e>` is the excerpt's index in the
manifest, two digits:

- `<e>_<case>.post`: little endian. Header: the 8 bytes `KANPOST1`, then
  int32 frames, int32 88, int32 264. Then float64 `time_ms[frames]`, then
  float32 `onset[frames*88]`, float32 `note[frames*88]`, float32
  `contour[frames*264]`, each row major by frame.
- `<e>_<case>.score.csv`: one line per score note, in the order of the
  results: `note,midi,score_onset_ms,score_end_ms,right,sounding,truth_onset_ms`.
  `right`, `sounding` and `truth_onset_ms` are the same values the notes
  CSV carries. They are for the analysis only: the path must not read
  them.
- `index.csv` in the directory: `e,excerpt,case,frames,notes`.

Works with `--jobs`. Add a selftest check that writes one dump of the
selftest's synthetic piece to a temporary directory, reads it back and
compares frames, the first and last `time_ms` and one posterior value.

## Build and runs (bounded)

```
nice cmake --build game/forge/native/build -j 6
ctest --test-dir game/forge/native/build
```

Never a bare `-j`. Then, with `--jobs 6`, paths `bp`, cases
`right,semitone,tone,octave,added,left_out,late120,jitter60`:

- the development manifest, dump to
  `/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4c-posteriors/dev/`;
- the held-out manifest, dump to `.../wp4c-posteriors/heldout/`.

These outputs are never committed.

## Final summary

Files written, total size, and proof that the summary of a run with the
option equals the one without. Commit as
`git -c user.name=KairoPhos -c user.email=kairophos@proton.me`, in
English, no push. Do not end while a background command runs.
