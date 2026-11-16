# libkan summit: plan, progress and findings

The working file of issue 41 (`docs/issues/41-polyphonic-live-play.md`).
Any session or agent picks the work up from here. Keep it short and
current: update the status table and add findings as they are measured.

- Design of record: `docs/architecture/polyphonic-live-measurement.md`.
- System design, practices and policies:
  `docs/implementation/libkan-summit-engineering.md`.
- Briefs, one per package: `docs/implementation/libkan-summit-briefs/`.
- Mission brief (local, not committed):
  `tools/dataset_bench/data/brief-fable-libkan-summit.md`.
- Rule for every number: it comes from the truth bench, with its
  negative controls, on the held-out list.

## 1. Goal

libkan measures a real performance well enough for a fair judge, piano
included: one lane per expected key, wrong notes rejected, the attack
time measured, in real time. Targets: the acceptance list of issue 41.

## 2. How the work is divided

- **Architect** (a strong model): design, briefs, review, decisions, this
  file. Reads every diff and runs every bench itself.
- **Builder** (MiMo through `opencode2`): one work package per brief, in
  its own worktree and branch. See `docs/agent-workflow.md`.
- **Owner**: product decisions and QA.

A work package below is sized so a builder can finish it from its brief
alone. "Needs" names what must be merged first. "Skill" says who can
take it: B = builder with a precise brief, A = architect level (design
judgement or DSP research inside).

## 3. Work packages and status

| # | Package | Needs | Skill | Status |
|---|---|---|---|---|
| 0 | Orientation and design note | | A | done 2026-11-15 |
| 1 | Truth bench, selftest, manifests, baseline of `blind`, `expect`, `core` | | B | done 2026-11-15, merged. Baseline of 12 excerpts and one-voice dumps taken (findings) |
| 2 | Learned evidence in the bench: a `bp` path on offline Basic Pitch posteriors (written note against rivals, attack time), then the same with emulated look-ahead of 50 to 500 ms | 1 | B | done 2026-11-16, merged (findings) |
| 3 | Harmonic evidence in the bench: attack curve from short windows, pitch from the tail with long windows, rivals in the joint fit. Prototype as a bench path first | 1 | A then B | prototype measured on the development list (`agent/harmonic-evidence`), see findings |
| 4 | Fusion rule of 2 and 3, tuned on the development list, reported on the held-out list | 2, 3 | A | open |
| 5 | `ChartMeasurer` in libkan: lanes (provisional attack, final fact, level, close), clock map, offline tests with known input | 4 | B, reviewed by A | open |
| 6 | Streamed Basic Pitch (incremental CQT and convolutions, causal normalisation), if package 2 shows it earns its place | 2 | B | open |
| 7 | C API: chart feed, clock map, lane events. `chart` path in the bench | 5 | B | open |
| 8 | Game: feed the chart, consume lane facts, the judge's wait follows the fact's deadline. MAESTRO levels end to end in the isolated instance, autoplay, `bench-play.sh` for the ocarina | 7 | B, reviewed by A | open |
| 9 | Effort by density, scheduled ahead. Deck budget | 5 | A then B | open |
| 10 | Documents: issue 41, validation status, libkan README as a library | 8 | B | open |
| 11 | Policy layer as an optional libkan module (section 5, part 3) | 5 | A then B | open |

## 4. Findings (dated, measured or read from the code)

- 2026-11-15. The judge (`LiveJudge`) pairs blind "played" notes with
  score notes. A chart-anchored measurement does that pairing inside
  libkan by construction, so the game side can become thinner.
- 2026-11-15. Basic Pitch's input filters last 256 samples at each
  octave's own rate: about 12 ms at the top octave and about 3 s at the
  lowest. A middle note's fundamental looks about 190 ms ahead. Its
  convolutions need 10 frames (116 ms) of context for the full onset
  path. It normalises each 2 s window by that window's minimum and
  maximum, which a stream cannot do as is. All three must be measured
  (package 2) before a streamed version is built (package 6).
- 2026-11-15. Basic Pitch was trained on MAESTRO's train split. Only the
  validation and test splits can speak for it. The Chopin Nocturne of the
  issue is in the validation split.
- 2026-11-15, owner. The one-voice path may change if this work finds a
  bug in it. Every change is explained and measured.
- 2026-11-15, owner. A low piano note rings on after a short key press.
  Time comes from the attack, pitch from the tail. Family physics is
  profile data.

- 2026-11-15, baseline of the truth bench. First 4 held-out excerpts,
  214 s, 1038 notes, none above 10 sounding. Builder's run, reproduced by
  the architect on the Nocturne. Output (local):
  `tools/dataset_bench/data/truth-bench/baseline-heldout/`.

  | Path | Right accepted | Wrong rejected: semitone / tone / octave / added | Attack error of accepted |
  |---|---|---|---|
  | `blind` (the game today) | 0.7 % (12.7 % not measured, 86.1 % absent) | 100 / 100 / 100 / 100 (it hears almost nothing) | median +9.7 ms, p95 17 ms |
  | `expect` | 4.4 % | 99.4 / 99.7 / 97.4 / 97.8 | drifts with a shifted score (late120: median 248 ms) |
  | `core` (the old claim) | 95.7 % | 31.3 / 34.3 / 12.0 / 7.1 | measures no time |
  | `oracle` | 100 % | 100 | 0 |
  | `yes` | 100 % | 0 | reads the shift exactly |

  CPU, share of real time, median of 3 pinned repeats: `blind` 30 %,
  `expect` 41 %, `core` 24 %. `expect` refuses 78 % of its chord sets.
- 2026-11-16, the baseline on all 12 held-out excerpts (638 s, 6260
  notes, none above 10 sounding), one run, no CPU repeats. This is the
  baseline of record. Output (local):
  `tools/dataset_bench/data/truth-bench/baseline-heldout-12/`.

  | Path | Right accepted | Not measured | Absent | Wrong rejected: semitone / tone / octave / added |
  |---|---|---|---|---|
  | `blind` | 0.2 % | 8.2 % | 90.9 % | 99.9 / 100 / 100 / 100 |
  | `expect` | 4.1 % | 14.5 % | 80.0 % | 98.3 / 98.7 / 97.8 / 97.9 |
  | `core` | 89.6 % | 1.7 % | 8.7 % | 32.3 / 33.1 / 18.5 / 18.2 |
  | `oracle` | 100 % | 0 | 0 | 100 (and 94.7 % accepted on `late120`) |
- 2026-11-15. The `late250` case is beyond the judge's own window: the
  oracle accepts only 44 % there (the meh window can be 240 ms, and a
  repeated pitch pairs with the wrong attack). Read every path's
  `late250` against the oracle's, and use `late120` (oracle 98 %) as the
  proof that time is measured.
- 2026-11-15. The data folder held audio for only 2 of the 12 held-out
  recordings. The builder fetched the other 10 (921 MB) from the official
  MAESTRO v3 archive into `tools/dataset_bench/data/maestro-v3.0.0/`.
  The development list's audio is not all there yet: package 2 needs it.
- 2026-11-15. A full run of all paths, cases and 3 repeats on 12 excerpts
  takes about 100 minutes. Run recognition without repeats, and CPU apart.

- 2026-11-15. One-voice event dumps of the five starter references
  (`wind_bowed` and `keyboard` profiles) are in
  `tools/dataset_bench/data/truth-bench/one-voice-dumps/`, with their
  SHA-256 and the commit they were taken at. A second run is byte
  identical. Compare after every libkan change.
- 2026-11-16. Harmonic evidence, prototype (`harm` path, branch
  `agent/harmonic-evidence`, not merged). Three development excerpts,
  1381 notes. Numbers are from the development list, so they are a
  direction, not a result.
  - Version 1, one long spectrum per note and a count of "new peaks":
    55 % of right notes could not be measured, because the partials
    shared with other written notes were left out too widely. Dropped.
  - Version 2, a window per partial as short as still parts a semitone
    there (42 periods: about 20 ms at 2 kHz, 170 ms at 250 Hz), still
    counting peaks with three thresholds each: a partial passed all
    three only 48 % of the time on right notes. Counting is too brittle
    in dense music. Dropped.
  - Version 3, the new level of each partial (after minus before the
    attack) weighed against the rival's same partial: at the first
    settings 55 % of right notes accepted, 6 % not measured, wrong notes
    rejected 99.3 % (semitone), 97.1 % (tone), 90.1 % (octave), 99.5 %
    (added). A threshold sweep on the features file reaches about 60 %
    accepted with every near rival above 95 %. Octaves stay near 90 %.
  - Attack time of accepted notes: median +6 ms, 85 % within 50 ms. The
    misses are a wrong candidate chosen, not a blurred one.
  - Piano partials sit about 10 cents above whole multiples (median, all
    registers, first 12 partials). The search range is -10 to +30 cents.
  - Reading: harmonic evidence alone is precise (it rejects) but finds
    too few right notes. It is the check, not the finder. Package 4
    joins it with the learned evidence.

- 2026-11-16, owner. Fair to right notes first: a right note penalised
  is worse than a wrong note forgiven. Tune for right notes accepted,
  report wrong notes rejected as it falls. This reorders the acceptance
  list of issue 41: target 4 (right notes found) leads, target 2 (wrong
  notes rejected) follows. For package 4 it means the fusion starts from
  "accept when either evidence supports the note and no rival is clearly
  stronger", not from "accept when both agree".
- 2026-11-16, owner. The tuning tracker of the game's judge moves into
  libkan (package 5): common instruments are out of tune and winds drift.
  The truth bench needs a detuned control for it: the same recording
  shifted by 20, 40 and 60 cents must score like the original (add with
  package 4; libkan has `pitch_shift.hpp`).

- 2026-11-16, owner, refining the rule above: forgive even past what
  is deserved rather than fall short, but a game that accepts everything
  and does not notice it is being fooled is no game. So: right notes
  accepted is the floor (aim at 98 % and above), and under that floor
  wrong notes rejected is pushed as high as it goes and always stated.
- 2026-11-16, learned evidence (package 2, merged). Basic Pitch's onset
  posterior of the written pitch, offline, on the 12 held-out excerpts
  (6260 notes). Output (local):
  `tools/dataset_bench/data/truth-bench/wp2-learned-evidence/`.

  | Rule, threshold | Right accepted | Wrong rejected: semitone / tone / octave / added |
  |---|---|---|
  | `plain` 0.30 | 96.3 % | 83.1 / 79.4 / 45.7 / 80.6 |
  | `plain` 0.40 | 92.1 % | 94.7 / 91.6 / 61.3 / 90.8 |
  | `rival` 0.40 | 84.6 % | 97.7 / 96.6 / 89.6 / 92.6 |

  - Attack time of accepted right notes: median +2 ms, 83 % inside the
    perfect window, p95 about 180 ms. The tail is a wrong peak taken
    (a repeated pitch), not a blurred one.
  - **Look-ahead costs nothing in accuracy.** With 100 ms of look-ahead
    every column is within 0.5 points of the whole recording. A live
    stream is limited by compute, not by latency: offline `infer` is
    14 % of one core, a window every 5 frames is about 30 times that.
    Package 6 (a real incremental stream) earns its place.
  - The `rival` rule buys octave rejection and pays in right notes: with
    a shifted score or notes outside the score, real notes become
    "rivals" (`late120` 69 %, `left_out` 80 %). Under the owner's rule it
    is the wrong default. A rival must be clearly stronger to veto.
  - Low register is weakest: 66.8 % accepted below midi 48 under
    `rival` 0.40, against 86 % in the middle and 90 % above.
  - Density: 86.6 / 85.6 / 78.8 % for 1, 2 to 4, 5 to 10 sounding.
  - On the development list, forgiving rules (accept from 0.15 to 0.2,
    veto only by a rival twice as strong) reach 99 % of right notes and
    reject 50 to 70 % of wrong ones. Not enough alone: the harmonic
    evidence has to supply the veto.

## 5. Decision: where the judge lives (owner approved, 2026-11-15)

The owner raised moving the judge into libkan (2026-11-15), for faster
answers and better verdicts. The rule so far was "libkan describes, the
game judges". The owner approved this proposal the same day. Part 3 is
built only after package 5 exists and can be measured:

1. **Pairing moves to libkan now.** Which sound belongs to which written
   note is the hard part, and the chart-anchored measurer does it. This
   is where response time and verdict quality are won.
2. **Policy stays a thin layer.** Windows, grades, mods, health and
   streaks are choices, not measurements. They map a lane's facts to a
   verdict in a few lines.
3. **That layer may ship in libkan as an optional module**, a pure
   function from facts to a grade, so other projects get a reference
   judge and replays judge the same everywhere. The measurer never
   depends on it.

What the API must carry whichever way it goes:

- the clock map between stream and chart, with rate, and a hold for
  "wait for the player";
- provisional facts fast enough for instant effects;
- lanes: many notes alive at once, each with its own id, for piano keys,
  guitar strings and other layouts;
- facts keyed by chart note, so any visual can be drawn from them.

## 6. Conventions for every brief

Files to read and code to reuse. Exact deliverables and tests. What not
to touch. Bounded builds (`-j 6`, `nice`). No `sed` or Python slice
edits on sources. Do not end while a background command runs. A short,
deep final summary with numbers. MAESTRO never leaves
`tools/dataset_bench/data/`. Commits as KairoPhos, no push.
