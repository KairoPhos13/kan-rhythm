# Work package 4e: the claim of `pair` follows the player's time

You are the builder. Read `docs/implementation/libkan-summit-engineering.md`,
the findings of 2026-11-16 in `docs/implementation/libkan-summit-plan.md`
("attack time"), `game/forge/native/truth_path_pair.hpp`, and the study
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4c-posteriors/claim2.py`
(the reference; where it and this brief differ, the brief wins, say so).
Work only in `game/forge/native/`. Do not change `libkan/`,
`tools/level_pack`, `charts/`. The path must not read the truth.

## Deliverable

In `truth_path_pair.hpp`, change only the claim:

1. **Running time offset.** Before the claims, list the unambiguous
   notes: a score note with exactly one peak of its pitch of at least
   `pair-off-min` (0.3) inside its meh window. Its offset is that peak's
   time minus the score time. For a score note at score time `t`, the
   offset in force is the median of the last `pair-off-k` (15)
   unambiguous notes whose score time is at most `t - 300` ms, 0 when
   there is none. It is causal on purpose.
2. **Claim by closeness.** A note claims, among the unclaimed peaks of
   its pitch inside its meh window (around the score time, as now), the
   one with the highest `value * (1 - min(1, d / meh))`, where `d` is
   the distance of the peak from the score time plus the offset in
   force. `s` stays the claimed peak's value.
3. `--pair-claim strongest|closeness` (default `closeness`), so the old
   claim stays measurable. `--pair-off-k`, `--pair-off-min`.
4. Features file: add at the end `offset_ms` (the offset in force) and
   `n_peaks` (unclaimed peaks of the pitch that were in the window).
5. Selftest: a synthetic case with a repeated pitch where the strongest
   claim takes the wrong repeat and the closeness claim the right one;
   and one where every note is 120 ms late and the offset brings the
   claims back. Keep the existing floors.

Everything else (sustain, rival, decision) is unchanged.

## Build and runs (bounded)

```
nice cmake --build game/forge/native/build -j 6
ctest --test-dir game/forge/native/build
```

Never a bare `-j`. Runs with `--jobs 6`, path `pair`, cases
`right,semitone,tone,octave,added,left_out,late120,jitter60,detune20,detune40`,
with `--features`, development and held-out manifests, outputs under
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/wp4e-pair-claim/`
(never committed). Wait for them in the foreground.

Expected on the held-out list (to check against, not to tune to): right
notes inside the perfect window about 96 %, right accepted within half
a point of 98.4 %, rejections within two points of 86.5 / 84.7 / 60.0 /
67.6.

## Final summary

Both lists: right accepted, the four rejections split in wrong pitch
and nothing played, left_out, late120, jitter60, detune20, detune40;
the attack error of accepted right notes for right, late120 and
jitter60 (median, share in the perfect window, p95). Differences from
the expected row, explained. Commit as
`git -c user.name=KairoPhos -c user.email=kairophos@proton.me`, in
English, no push. Do not end while a command runs.
