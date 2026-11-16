# Work package 4f: clear and grey refusals, doubt, and the policy check

You are the builder. Read `docs/implementation/libkan-summit-engineering.md`,
`docs/implementation/libkan-summit-game-integration.md` (sections 2 to
4 are the specification), the plan's findings of 2026-11-16 ("zero
misses on right play"), and in `game/forge/native/`:
`truth_path_pair.hpp`, `truth_bench.hpp`, `kan_truth_bench.cpp`,
`kan_truth_selftest.cpp`. The study to reproduce:
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/levels/lv.py`,
`lv2.py`, `lv5.py` (where they and the specification differ, the
specification wins; say so). Work only in `game/forge/native/`. Do not
change `libkan/`, `tools/level_pack`, `charts/`. No path reads the
truth.

## Deliverables

1. `pair` marks each refusal clear or grey (specification 2.7). In the
   notes CSV and the summary the outcomes of `pair` are Accepted,
   Misreported (clear, with the rival's pitch) and Absent (grey). Say in
   the header comment that Absent now means a grey refusal for this
   path. Settings `--pair-clear-lead` (0.6), `--pair-clear-sustain`
   (0.4).
2. Doubt per score note (specification 2.8), from the case's score
   only. Coefficients as a struct with the defaults given, in a new
   small header `truth_doubt.hpp`. Features file: add `clear`, `doubt`,
   `pace_ms`, `chord_size` at the end.
3. **Policy check** (new header `truth_policy.hpp`, used by the bench
   after a path ran, for paths that report grey refusals; today only
   `pair`): the hidden budget of specification 4 over the notes in
   score order. Settings `--policy-capacity` (5), `--policy-gain` (12),
   `--policy off`. The summary gets a new table per path and case:
   right notes: accepted, spared, misses; wrong notes: credited,
   spared, misses (counts and shares). The notes CSV gets a last column
   `judged` (ok, spared, miss).
4. A manifest `truth_manifest_levels.txt` is NOT committed (the level
   files are local). Instead the bench accepts absolute paths in a
   manifest given with `--manifest`, if it does not already.
5. Selftest: unit checks of clear against grey, of the doubt formula on
   three hand-computed notes, and of the budget (spends, refills by
   doubt, never above capacity, a clear refusal is always a miss, right
   play of the synthetic piece ends with zero misses).

## Build and runs (bounded, in the foreground)

```
nice cmake --build game/forge/native/build -j 6
ctest --test-dir game/forge/native/build
```

Never a bare `-j`. Runs, `--jobs 6`, path `pair`, cases
`right,semitone,tone,octave,added,left_out,late120,jitter60`, outputs
under
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4f-policy/`
(never committed): the development manifest, the held-out manifest, and
the three levels (audio and truth in
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/levels/`:
`chopin-nocturne-op9-2` 0 to 93 s, `chopin-etude-op25-10` 0 to 63 s,
`liszt-wilde-jagd` 0 to 50 s, each `.wav` with its `.mid`).

Expected (to check against, not to tune to): zero misses on the right
case everywhere; wrong notes that end as a miss on the held-out list
about 54.9 / 52.9 / 23.0 / 22.7 %, Nocturne 77.3 / 72.3 / 46.0 / 36.8,
Etude 18.8 / 15.4 / 8.8 / 3.5, Wilde Jagd 22.5 / 24.0 / 8.4 / 9.6.

## Final summary

The policy table for the five inputs, differences from the expected
rows explained, what surprised you. Commit as
`git -c user.name=KairoPhos -c user.email=kairophos@proton.me`, in
English, no push. Do not end while a command runs.
