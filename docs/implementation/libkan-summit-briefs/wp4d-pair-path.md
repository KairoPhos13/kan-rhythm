# Work package 4d: the `pair` path of the truth bench

You are the builder. Read `docs/implementation/libkan-summit-engineering.md`,
the findings of 2026-11-16 in `docs/implementation/libkan-summit-plan.md`
("pairing first"), and in `game/forge/native/`: `truth_bench.hpp`,
`truth_paths.hpp`, `truth_paths_bp.hpp`, `kan_truth_bench.cpp`,
`kan_truth_selftest.cpp`. A reference of the study in Python is at
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4c-posteriors/pair.py`
(read it; where this brief and the script differ, the brief wins, and
say so). Work only in `game/forge/native/`. Do not change `libkan/`,
`tools/level_pack`, `charts/`. The path must not read the truth.

## Deliverable: path `pair` (new file `truth_path_pair.hpp`)

Offline Basic Pitch posteriors, as `bp` takes them. Then:

1. **Peaks.** For every pitch, each frame where the onset posterior is a
   local maximum (>= the frame before, > the frame after) and at least
   `peak_min` (0.15).
2. **Claim.** Score notes in score order. A note claims the strongest
   unclaimed peak of its pitch inside its meh window. `s` is that peak's
   value and the attack time is its frame time. With no peak, `s` is the
   highest onset posterior of the pitch in the meh window and the attack
   time is the score time.
3. **Own sustain.** `na`: mean note posterior of the pitch over frames
   attack+1 to attack+12. `nmax`: highest note posterior of the pitch
   from the attack frame to the written end, at most 1000 ms after the
   score onset.
4. **Rival.** Among unclaimed peaks at -12, -2, -1, +1, +2, +12
   semitones whose time is inside the note's perfect window around the
   score onset, leaving out a peak within 6 frames of a claimed peak of
   its own pitch: the strongest. `u` its value, `un` the mean note
   posterior of the rival pitch over its frames +1 to +12, `u_midi` its
   pitch. None: `u` = `un` = 0.
5. **Decision** (settings on the command line, defaults in brackets):
   `real` = `u >= pair-u` (0.3) and `un >= pair-un` (0.15) and
   `u >= s + pair-c` (0.3).
   Accepted when (`nmax >= pair-a` (0.13) and not (`real` and
   `un >= nmax + 0.15`)) or (`s >= pair-b` (0.25) and not `real`).
   Not accepted: **Misreported** with the rival's pitch when `real`,
   else **Absent**. These two must stay apart in every output.
6. **Features file** (`--features`): excerpt, case, note, right,
   sounding, midi, score_onset_ms, truth_onset_ms, measured_attack_ms,
   s, na, nmax, u, un, u_midi, found (1 when a peak was claimed), real.
7. `uses_score()` true, `reports_cpu()` as `bp`. Works with `--jobs` and
   with `--dump-posteriors` untouched.
8. **Selftest.** Unit checks of the decision on hand-written values
   (each clause), and on the synthetic piece: right accepted >= 98 %,
   semitone rejected >= 80 %.

## Build and runs (bounded)

```
nice cmake --build game/forge/native/build -j 6
ctest --test-dir game/forge/native/build
```

Never a bare `-j`. Runs with `--jobs 6`, path `pair`, cases
`right,semitone,tone,octave,added,left_out,late120,jitter60,detune20,detune40`,
with `--features`, on the development and the held-out manifests,
outputs under
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4d-pair/`
(never committed).

Expected on the held-out list (from the study, to check against, not to
tune to): right 98.4 %; rejected 86.5 / 84.6 / 59.8 / 67.6.

## Final summary

The two tables (development, held-out) with right accepted, the four
rejections split in misreported and absent, left_out, late120,
jitter60, detune20, detune40, and the attack error of accepted right
notes (median, share in the perfect window, p95). Any difference from
the expected row, explained. Commit as
`git -c user.name=KairoPhos -c user.email=kairophos@proton.me`, in
English, no push. Do not end while a background command runs.
