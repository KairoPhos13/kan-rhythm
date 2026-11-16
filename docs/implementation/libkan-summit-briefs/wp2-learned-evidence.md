# Work package 2: learned evidence in the truth bench (issue 41)

Needs work package 1 merged (the truth bench). You are the builder. Work
ONLY in your own git worktree and branch (`agent/learned-evidence`),
never in the main folder. Everything you write is English.

## Goal

Measure, with the truth bench and its negative controls, what Basic
Pitch's posteriors can answer about a written note: was it struck, was a
rival struck instead, and when. First with the whole recording (the
ceiling), then with a limited look-ahead (what a live stream could have).
No libkan change, no C API change: this is a measurement.

## Read first

- `docs/architecture/polyphonic-live-measurement.md` (sections 2 and 3).
- `docs/implementation/libkan-summit-plan.md` (findings on Basic Pitch).
- `game/forge/native/truth_bench.hpp`, `kan_truth_bench.cpp` and the
  paths of work package 1: how a path is written and scored.
- `libkan/include/libkan/basic_pitch.hpp` and
  `libkan/src/basic_pitch.cpp`: `infer`, `infer_window`, `resample`,
  `Posteriorgrams::frame_time`, the window constants (43844 samples at
  22050 Hz, 172 frames, hop 256).
- Scratch to start from (read only, main folder):
  `tools/dataset_bench/data/harness-2026-11-15/bp.cpp`.

## Deliverables (in `game/forge/native/`)

1. A path `bp` in the truth bench.
   - Posteriors: `kan::basic_pitch::infer(audio, n, 48000, 1)` once per
     excerpt (it does not depend on the score).
   - For each score note (written pitch m, score time t, meh window W,
     in score time order):
     - attack: the highest value of the onset posterior of m over frames
       within [t - W, t + W] that are not already used by an earlier
       score note of the same pitch (a used peak blocks 3 frames either
       side). Its frame time is the measured attack, its value is the
       support `s`.
     - rivals: m ± 1, m ± 2, m ± 12 (inside 21..108), leaving out any
       pitch the SCORE itself has within the perfect window of the
       measured attack. Rival support `r` = the highest onset posterior
       of any rival within 3 frames of the measured attack; keep which
       rival.
     - also record `n_after` = mean note posterior of m over the 10
       frames after the attack, and `n_before` = the same over the 10
       frames before it (a note that already rang has both high).
   - Decision with two parameters, threshold `th` and rule:
     - rule `plain`: Accepted when s >= th;
     - rule `rival`: Accepted when s >= th and s >= r;
     - not accepted: Misreported (midi = the rival) when r >= th and
       r > s, else Absent.
   - Options `--bp-th 0.3 --bp-rule rival` (these are the defaults).
2. A features file: with `--features <file.csv>` the `bp` path writes one
   row per excerpt, case and score note: right, sounding, midi, score
   onset, truth onset, measured attack, s, r, rival midi, n_before,
   n_after. The architect designs the fusion from this file, so it must
   be complete and exact.
3. A sweep mode `--bp-sweep`: one table, a line per (rule, th in 0.05,
   0.1, 0.2, 0.3, 0.4, 0.5, 0.6), with for "up to 10 sounding": right
   accepted % (case `right`), wrong rejected % for each of `semitone`,
   `tone`, `octave`, `added`, right accepted % for `left_out`, `late250`
   and `jitter60`, and the attack error (median, p95, % in the perfect
   window, % within 50 ms) for `right` and `late250`.
4. A path `bp-la` with `--bp-lookahead <ms>`: the same decision on
   posteriors that could exist with that look-ahead.
   - Resample once to 22050 Hz (`kan::basic_pitch::resample`).
   - Work in chunks of 5 frames. For each chunk, build one 43844-sample
     window that ENDS at the chunk's last frame time plus the look-ahead
     (samples before the recording are zero), run `infer_window`, and
     keep only that chunk's 5 frames. So every kept frame saw between L
     and L + 58 ms of the future and nothing more.
   - Frame i of a window sits at window sample i * 256. Use the same
     time for the offline path's frames when comparing.
   - Check and print: with `--bp-lookahead 1000` the mean and the worst
     absolute difference of the onset posterior against the offline
     `infer` on the same excerpt. Explain any difference above 0.02.
   - This costs about 30 times the offline network. Add `--jobs N` (at
     most 6) to run excerpts in parallel for paths where CPU time is not
     reported. Print the CPU of `bp` (offline, one thread) as share of
     real time.
5. The selftest gains: `bp` runs on the synthetic piece and its counts
   add up; a sweep line with th 0 and rule `plain` accepts 100 % of
   right notes.

## Runs to do and report

Outputs under
`/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/`.
Always `nice`, at most 6 jobs.

1. `--bp-sweep` on the development manifest and on the held-out manifest
   (all cases), with `--features`.
2. `bp-la` with look-ahead 100, 200 and 400 ms on the held-out manifest
   at the best (rule, th) of the development sweep, all cases.
3. The 1000 ms check on one excerpt.

## Rules

- Touch only `game/forge/native/`. Not `libkan/`, not the C API, not C#,
  not `tools/`, `charts/`, `docs/`.
- The path sees the audio and the case's score only, never the truth.
- MAESTRO is CC BY-NC-SA: never copy audio, MIDI or outputs into the
  worktree, never commit them.
- Bounded builds: `nice cmake --build <dir> -j 6`. Never a bare `-j`.
  Do not delete or rebuild any build folder in the main folder.
- No `sed`, no Python slice edits on source files.
- No clause-separating dashes in comments and commit messages.
- Commit with
  `git -c user.name=KairoPhos -c user.email=kairophos@proton.me commit`.
  No other name or email anywhere. Do not push.
- Do not end while a command of yours still runs in the background.

## Final summary (short, deep, with numbers)

- the sweep tables (development and held-out);
- the look-ahead table: 100, 200, 400 ms against offline;
- the 1000 ms check;
- CPU of the offline network;
- by register (midi below 48, 48 to 71, 72 and above): right accepted
  and attack error at the chosen setting;
- what you could not do or had to decide yourself, and why.

## Notes from work package 1 (2026-11-15)

- The audio of 9 of the 12 development recordings is not in
  `tools/dataset_bench/data/maestro-v3.0.0/` yet (the held-out audio is).
  Fetch the missing files first, the way package 1 did: by HTTP range
  from the official `maestro-v3.0.0.zip`
  (`https://storage.googleapis.com/magentadata/datasets/maestro/v3.0.0/maestro-v3.0.0.zip`)
  into that data folder only. Never download the whole 100 GB archive.
- The `late250` case is beyond the judge's window (the oracle accepts
  44 % there). Report it next to the oracle's number.
- A run with every path, case and 3 repeats is slow. Run recognition
  with `--repeat 1`, and only the paths you need.
- The build folder of package 1 used `-DKAN_ENABLE_ROFORMER=OFF`. Do the
  same in your worktree.
