# Work package 1: the truth bench (issue 41)

You are the builder. Work ONLY in the git worktree
`/mnt/dev_workspace/WDS/kan-rhythm-agents/truth-bench` (branch
`agent/truth-bench`). Never edit `/mnt/dev_workspace/WDS/kan-rhythm` (the
main folder); you may READ data from it. Everything you write is English.

## Goal

A measurement tool, `kan_truth_bench`, that tells how well a live path
judges real piano, with negative controls. It drives the C ABI the game
calls (`kan_forge_live_*`). Plus a selftest in ctest that needs no dataset.

## Read first

- `docs/architecture/polyphonic-live-measurement.md` section 2: this is
  the specification. Follow it exactly. This brief adds the details.
- `docs/issues/41-polyphonic-live-play.md`.
- `game/forge/native/kan_forge_api.h` (the live section at the end) and
  `kan_forge_api.cpp` (`kan_forge_live_*`).
- `game/forge/native/kan_forge_bench.cpp`: reuse its `read_midi` and its
  style. Move the shared readers into a small header
  `game/forge/native/bench_io.hpp` used by both tools (keep
  `kan_forge_bench` working).
- `game/forge/native/kan_forge_selftest.cpp` and `CMakeLists.txt` (how a
  tool and a test are added, `kan_first_party_target`, miniaudio include).
- `game/core/Play/LiveJudge.cs` lines 55 to 130: `PaceOf`, `WindowMs`,
  the constants. Port them to C++ as they are, with a comment naming the
  C# source.
- `libkan/include/libkan/score_verify.hpp` (`measure_score`, `ScoreNote`).
- Scratch harness to start from (read only, in the main folder):
  `/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/harness-2026-11-15/polybench.cpp`.

## Deliverables (all new files in `game/forge/native/`)

1. `bench_io.hpp`: MIDI reader (from `kan_forge_bench.cpp`), audio loader
   through miniaudio to 48 kHz mono float (any wav; MAESTRO is 44.1 kHz
   stereo). Do not define `MINIAUDIO_IMPLEMENTATION` a second time if the
   library already exports the decoder symbols; check how
   `kan_forge_selftest.cpp` does it and do the same.
2. `truth_bench.hpp`: the engine, header only, no I/O, so the selftest
   can use it:
   - `struct TruthNote { double onset_ms, dur_ms; int midi; }`.
   - `struct ScoreNote { double onset_ms, dur_ms; int midi; bool right;
     int truth_index; }` (`right` false = a wrong note: changed or added;
     `truth_index` = the truth note it came from, or -1 for added).
   - Case builders from the truth, with a seed (use `std::mt19937`, fixed
     seed 7 by default): `right`, `semitone`, `tone`, `octave` (30 % of
     notes moved by ±1, ±2, ±12, random sign, kept inside 21..108),
     `added` (30 % more notes: each copies a random truth note's onset
     and duration with a pitch 1 to 12 semitones away, random sign),
     `left_out` (30 % removed), `late120`, `late250`, `early120` (whole
     score shifted), `jitter60` (each note moved by a uniform ±60 ms).
     A changed or added note is valid only when the truth has NO attack of
     its new pitch within its meh window around its score time. Redraw up
     to 8 times, else leave that note unchanged (count how many).
   - The judge's windows: pace of each score note (`PaceOf`, chord = 30
     ms) and `WindowMs` for perfect and meh.
   - `enum class Outcome { Accepted, Misreported, Absent, NotMeasured }`
     and `struct NoteResult { Outcome outcome; double onset_ms; float
     midi; }` (onset_ms = the path's measured attack in stream time, NaN
     when none).
   - Scoring: tables as in the design note, by notes sounding at the
     attack: buckets 1, 2-4, 5-10, 11+, and a line "up to 10". Sounding =
     truth notes with onset <= t < onset + dur at the note's truth onset
     (for an added note, at its score onset). Per bucket:
     - right notes: count, accepted %, not measured %, misreported %,
       absent %;
     - wrong notes: count, rejected % (rejected = any outcome but
       Accepted);
     - attack time error of accepted right notes = measured onset minus
       TRUTH onset (not the score's): median, p95 of |error|, % within the
       note's perfect window, % within 50 ms. A path that measures no time
       reports its onset as NaN and these print `n/a`.
   - An abstract path: `struct Path { virtual const char* name(); virtual
     bool uses_score(); virtual void run(audio, sr, score, results,
     cpu); }`. `cpu` collects the wall time of every 10 ms
     `kan_forge_live_process` call with the sounding count at that time.
3. Paths (`truth_paths.hpp` or in the tool):
   - `blind`: `kan_forge_live_open("keyboard", 48000)`, feed 480-sample
     blocks. A played note = a HIT plus its latest PITCH (final replaces
     provisional; unresolved or midi <= 0 = no pitch). Pair to score notes
     in score time order: the nearest unclaimed played note with a pitch
     within 50 cents of the written one and an onset within the meh window
     of the score time = Accepted (claim it). Else an unclaimed played
     note with no pitch inside the meh window = NotMeasured. Else an
     unclaimed played note with another pitch inside the PERFECT window =
     Misreported. Else Absent. The audio pass does not depend on the
     score: run it once per excerpt and pair it against every case.
   - `expect`: the same, and before each chord (score notes within 30 ms)
     call `kan_forge_live_expect` 40 ms ahead, as polybench does. Count
     refused sets. This pass depends on the score.
   - `core`: `kan::score_verify::measure_score` on the case's score
     (keyboard profile, default fit step). Accepted when
     `sim_wsum / sim_w >= 0.60` and `|cents_center| <= 50` and not
     saturated; NotMeasured when saturated; else Absent. Onset NaN. CPU =
     total time over the audio length.
   - `oracle`: knows the truth. Accepted at the truth onset when the truth
     has an attack of the written pitch within the meh window (nearest,
     one truth note serves one score note); else Misreported when the
     truth has another pitch within the perfect window; else Absent.
   - `yes`: accepts every score note at its score time.
   Leave a clear place to add a `chart` path later.
4. `kan_truth_bench.cpp`: the tool.
   - `--maestro <dir>` and `--manifest <file>`: lines
     `audio.wav midi.midi from_s to_s` relative to the dir, `#` comments.
   - or `--audio a.wav --truth a.midi --from 0 --to 60` for one excerpt.
   - `--paths blind,expect,core,oracle,yes` (default `blind,core`).
   - `--cases all` or a list. `--seed N`.
   - `--out <dir>`: writes `summary.txt` (the tables: one per path and
     case, plus one table per path that puts every case on one line for
     "up to 10") and `notes.csv` (one row per excerpt, path, case, score
     note: outcome, right, sounding, score onset, truth onset, measured
     onset, midi, reported midi).
   - `--repeat N` (default 1): repeats the CPU measuring pass of the
     `right` case and prints min, median and max across repeats of: share
     of real time, per block median, p99, worst. Print a note that CPU
     numbers mean something only when run alone under `taskset`.
   - `--dump-events <audio> --profile <name>`: prints every event of
     `kan_forge_live_process` (10 ms blocks) as one text line with all the
     fields (`%.9g`), nothing else. Used to compare a path before and
     after a change.
   - `--make-manifests <maestro-v3.0.0.csv>`: writes
     `truth_manifest_dev.txt` (12 recordings from the `train` split) and
     `truth_manifest_heldout.txt` (6 `validation` + 6 `test`), chosen with
     seed 7 among recordings of at least 150 s, excerpt 30 s to 90 s. The
     held-out list also starts with the Chopin Nocturne Op. 9 No. 2 (2011,
     `MIDI-Unprocessed_06_R3_2011_MID--AUDIO_R3-D3_05_Track05_wav`) from 0
     to 60 s. Parse the CSV with care: fields are quoted and hold commas.
     Commit the two manifests (file names only, no data).
5. `kan_truth_selftest.cpp`, added to ctest as `kan_truth_selftest`:
   - builds a synthetic piano-like piece, about 20 s, fixed seed: notes
     with 8 partials at `k*f0*sqrt(1+B*k*k)` (B = 0.0004), a 3 ms attack
     and an exponential decay, 1 to 6 notes at once, MIDI 36..96;
   - checks: every case has the expected number of right and wrong notes
     and the outcome counts add up; `oracle` accepts 100 % of right notes
     and rejects 100 % of wrong notes in every case, with time error 0;
     `yes` accepts 100 % of right notes and rejects 0 % of wrong notes;
     the shifted cases show `yes` time errors of exactly the shift;
     `blind` runs through and its counts add up (no quality threshold);
     the event dump of the same audio twice is identical.
6. `CMakeLists.txt`: add the tool and the test, with
   `kan_first_party_target`. No new warnings (`-Wall -Wextra`).

## Build and run (bounded, always)

```
cd /mnt/dev_workspace/WDS/kan-rhythm-agents/truth-bench
cmake -S game/forge/native -B game/forge/native/build -DCMAKE_BUILD_TYPE=Release \
  -DFETCHCONTENT_SOURCE_DIR_MINIAUDIO=/mnt/dev_workspace/WDS/kan-rhythm/game/forge/native/build/_deps/miniaudio-src
nice cmake --build game/forge/native/build -j 6 --target kan_truth_bench kan_truth_selftest kan_forge_bench kan_forge_selftest
ctest --test-dir game/forge/native/build --output-on-failure -R "kan_truth_selftest|kan_forge_selftest"
```

Try `-DKAN_ENABLE_ROFORMER=OFF` first: if it configures and links, use it
(much lighter). If not, build the default. NEVER a bare `-j`. Never more
than 6 jobs. Do not delete or rebuild any build folder in the main
folder.

Then run the baseline on real data (read only source, outputs to the main
folder's ignored data dir):

```
M=/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data
B=game/forge/native/build
$B/kan_truth_bench --make-manifests $M/maestro-v3.0.0/maestro-v3.0.0.csv
nice taskset -c 10 $B/kan_truth_bench --maestro $M/maestro-v3.0.0 \
  --manifest game/forge/native/truth_manifest_heldout.txt \
  --paths blind,expect,core,oracle,yes --cases all --repeat 3 \
  --out $M/truth-bench/baseline-heldout
```

If one full run takes more than 40 minutes, say so and run only the first
4 lines of the manifest; do not skip the controls.

## Rules

- Touch only `game/forge/native/` (new files, `CMakeLists.txt`, and the
  small move of readers out of `kan_forge_bench.cpp`). Do not touch
  `libkan/`, `kan_forge_api.cpp`, `kan_forge_api.h`, `tools/`, `charts/`,
  `docs/`, C# code.
- MAESTRO is CC BY-NC-SA: never copy audio, MIDI or outputs into the
  worktree, never commit them. Outputs go under
  `/mnt/dev_workspace/WDS/kan-rhythm/tools/dataset_bench/data/truth-bench/`.
- No `sed`, no Python slice edits on source files: use your edit tool.
- No clause-separating dashes in comments and commit messages. Short,
  plain English.
- Commit on `agent/truth-bench` with
  `git -c user.name=KairoPhos -c user.email=kairophos@proton.me commit`.
  Never write any other name or email. Do not push.
- Do not end while a command of yours still runs in the background. Wait
  for it and read its result.
- The code must be honest: no path may read the truth except `oracle`.
  The `blind`, `expect` and `core` paths see only the audio and the
  case's score.

## Final summary (short, deep, with numbers)

- what you built, file by file, and the commits;
- the selftest result;
- the baseline tables for the held-out list: for each path, the line "up
  to 10" of every case (right accepted, wrong rejected, time error, not
  measured), and the CPU lines with their spread;
- how long the run took;
- anything in the specification you could not do or had to decide
  yourself, and why.
