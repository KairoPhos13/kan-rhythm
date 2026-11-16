# Work package 4b: the fair rule, the contour witness and the detuned control

You are the builder. Read first, in this order:

- `docs/implementation/libkan-summit-engineering.md` (rules of the work);
- `docs/implementation/libkan-summit-plan.md`, section 4, the findings of
  2026-11-16 (learned evidence and fusion);
- `game/forge/native/truth_bench.hpp`, `truth_paths.hpp`,
  `truth_paths_bp.hpp`, `kan_truth_bench.cpp`, `kan_truth_selftest.cpp`;
- `libkan/include/libkan/basic_pitch.hpp` (`Posteriorgrams`: note, onset
  and contour, 264 contour bins, three per semitone from A0);
- `libkan/include/libkan/pitch_shift.hpp`.

Work only in this worktree, only in `game/forge/native/` and
`game/forge/native/CMakeLists.txt`. Do not change anything in `libkan/`,
`tools/level_pack`, `charts/`. No path may read the truth except
`oracle`.

## Deliverables

1. **Rule `fair` in the `bp` and `bp-la` paths** (`--bp-rule fair`).
   Accept a written note when its note posterior after the attack
   (`n_after`) is at least A, or when its onset support `s` is at least B
   and no rival has `r >= s + c`. Defaults A 0.13, B 0.25, c 0.3, each
   settable: `--bp-fair-a`, `--bp-fair-b`, `--bp-fair-c`. `plain` and
   `rival` keep their behaviour. `fair` becomes the default rule.
2. **Contour features.** For every score note add to the features CSV
   (new columns at the end, existing columns unchanged):
   - `c_own`: mean over the frames from the measured attack to 150 ms
     after it of the largest contour value in the three bins of the
     written pitch and their two neighbours (five bins);
   - `c_up1`, `c_dn1`, `c_up2`, `c_dn2`: the same for the pitches one and
     two semitones above and below;
   - `c_cents`: over the same frames, the contour-weighted mean offset in
     cents of the bins within plus or minus 70 cents of the written
     pitch, relative to the written pitch (empty when the weight is
     under 0.05). This is the reading a tuning tracker will use.
   When no attack was measured use the score time.
3. **Detuned control.** New cases `detune20`, `detune40`, `detune60`:
   the same audio as `right`, shifted up by 20, 40 and 60 cents with
   libkan's pitch shifter (keep the duration; if the shifter cannot keep
   it, resample and scale the score and truth times by the same factor,
   and say which you did). The score is unchanged. All notes are right
   notes. They are not in the default case list; they run when named in
   `--cases`.
4. **Selftest.** Add: `fair` accepts at least 98 % of the right notes
   and rejects at least 60 % of the semitone notes on the selftest's
   synthetic or bundled input, whichever the selftest already uses for
   `bp`. If the selftest has no audio for `bp`, add a pure unit test of
   the rule function on hand-written `NoteMeasure` values instead.

## Build and runs (bounded, always)

```
nice cmake --build game/forge/native/build -j 6
ctest --test-dir game/forge/native/build
```

Configure with `-DKAN_ENABLE_ROFORMER=OFF` if the build folder is new.
Never a bare `-j`. No full clean rebuild.

Runs, each with `--jobs 6`, outputs under
`tools/dataset_bench/data/truth-bench/wp4-fair/` (never committed):

1. development and held-out manifests, path `bp`, rule `fair`, cases
   `right,semitone,tone,octave,added,left_out,late120,jitter60`, with
   `--features`;
2. the same two lists, cases `right,detune20,detune40,detune60`, with
   `--features`.

## Final summary wanted (short, deep, with numbers)

The held-out table for run 1 (right accepted, the four rejections,
`left_out`, `late120`, `jitter60`). The table for run 2: right accepted
at 0, 20, 40, 60 cents, and the median of `c_cents` at each. Medians of
`c_own`, `c_up1`, `c_dn1` for right notes and for wrong semitone notes.
What surprised you. Commits as
`git -c user.name=KairoPhos -c user.email=kairophos@proton.me`, in
English, no push. Do not end while a background command runs.
