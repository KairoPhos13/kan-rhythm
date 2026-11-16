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
| 3 | Harmonic evidence in the bench: attack curve from short windows, pitch from the tail with long windows, rivals in the joint fit. Prototype as a bench path first | 1 | A then B | prototype v3 measured on both lists and not kept: it adds nothing to the learned evidence (findings 2026-11-16). Branch `agent/harmonic-evidence` stays unmerged. A new design is needed |
| 4 | Fusion rule of 2 and 3, tuned on the development list, reported on the held-out list | 2, 3 | A | measured 2026-11-16: the best forgiving rule uses the learned evidence only and is far from the summit (findings). Rule `fair`, contour features, detuned control (4b), posterior dump (4c) and the `pair` path (4d) merged (findings). `pair` is the path of record |
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

- 2026-11-16, fusion (package 4). The features of the learned path and
  of harmonic prototype v3 were joined note by note on both lists
  (56 144 development rows, 50 076 held-out rows, none lost). Rules were
  searched on the development list only. Held-out numbers:

  | Rule (thresholds from the development list) | Right accepted | Wrong rejected: semitone / tone / octave / added |
  |---|---|---|
  | learned only, floor 98 % on development | 96.9 % | 89.2 / 84.6 / 63.5 / 81.9 |
  | learned only, floor 99 % on development | 98.3 % | 78.3 / 74.9 / 47.1 / 69.5 |
  | learned only, floor 99.5 % on development | 99.5 % | 60.1 / 55.4 / 37.9 / 34.5 |
  | learned with harmonic vetoes, floor 99 % | 98.5 % | 78.8 / 74.4 / 47.5 / 65.7 |
  | harmonic rescues weak learned support, floor 98 % | 96.7 % | 89.7 / 86.5 / 59.3 / 84.4 |

  The learned rule: accept when the note posterior after the attack is
  at least A, or when the onset posterior s is at least B and no rival
  has r >= s + c. Floor 99 %: A 0.13, B 0.25, c 0.3.

  - **The note posterior is the better witness.** A right note's note
    posterior after its attack has a median of 0.54. A wrong note's
    stays at the model's resting level (0.10 to 0.12, 95th centile
    0.2). Package 2 used only the onset posterior. Any stream of the
    model (package 6) must carry both outputs.
  - **Harmonic prototype v3 is dropped as a fusion part.** Used as a
    veto, as a rescue or inside a fitted model (logistic and boosted
    trees) it moves no column by more than 2 points on the held-out
    list. Its features alone reject 41 / 42 / 8 / 32 % at a 98 % floor.
    The per-partial projection is too weak next to other notes.
  - **A setting loses about one point of right notes from the
    development list to the held-out list.** To hold 98 % on new music
    the floor on the development list must be 99 %.
  - **Where the right notes are lost:** below midi 48 (94.5 % accepted
    on development against 99.6 % above). Where the wrong ones pass:
    the octave (half of them, 89 % when the score has the octave
    written within 100 ms), and any wrong note whose pitch the score
    also writes within 1.5 s.
  - **Honest state against the summit.** Fair to right notes (98 %)
    today means about three of four wrong semitones and tones caught and
    half of the octaves. The summit asks 95 %. The gap is in the
    evidence, not in the rule: no rule over these features closes it.
  - Next levers, in the order of expected gain: (1) a harmonic check
    that fits the written notes and the rivals together over a long
    tail window (the joint fit of `HarmonicChordValidator`), asked only
    for notes the learned evidence accepts, aimed at the octave and at
    the low register; (2) the learned model's contour output (three
    bins per semitone), which the port already computes, as a semitone
    witness and as the reading for the tuning tracker; (3) the detuned
    control.

- 2026-11-16, package 4b (merged): rule `fair` is the default of the
  `bp` paths, the features file carries contour columns, and the cases
  `detune20`, `detune40`, `detune60` exist. The builder's held-out
  numbers equal the design's exactly (98.3 %; 78.3 / 74.9 / 47.1 /
  69.5). Outputs (local): `truth-bench/wp4-fair/`.

  | Recording shifted by | Right accepted (held-out / development) | Median contour reading |
  |---|---|---|
  | 0 cents | 98.3 / 99.1 % | -0.1 / +0.4 cents |
  | 20 cents | 97.9 / 98.7 % | +6.9 / +8.7 |
  | 40 cents | 94.2 / 96.3 % | +15.0 / +16.8 |
  | 60 cents | 66.0 / 68.8 % | +19.9 / +21.5 |

  - **Without a tuning tracker the learned path fails the owner's rule
    from 40 cents.** At 60 cents a quarter of the right notes are
    reported as the neighbour semitone. The tracker of package 5 is
    needed, not optional.
  - The contour reading follows the shift but compressed (slope about
    0.45 on piano, 0.66 on a pure tone): the model's resting level pulls
    it to zero. A tracker must read the contour's peak bin over a wider
    span, or calibrate the slope. The contour's centre bin of a pitch is
    `3 * (midi - 21) + 1`.
  - **The contour does not move the frontier.** Own contour level of a
    right note: median 0.39; of a wrong one: 0.12 to 0.13. But the right
    notes it misses are the ones the other two outputs miss. With it in
    the rule the held-out row at the 99 % floor is 98.3 %; 77.7 / 75.2 /
    48.4 / 67.2. The three outputs of the model fail together.
  - Next idea (package 4c dumps the posteriors to study it): judge a
    wrong note by positive evidence, not by weak support. Pair the
    model's attacks with the written notes first; a rival is then only
    an attack no written note explains. Today a rival may be the next
    note of a melody that moves by step.

- 2026-11-16, pairing first (study on the posterior dumps of package
  4c, `truth-bench/wp4c-posteriors/`, 1.7 GB local; scripts were scratch
  and the rule below is all that is needed to rebuild them). Every
  attack peak of the model (onset posterior at least 0.15) is first
  claimed by the written note of its pitch whose meh window holds it. A
  rival of a written note is then only an unclaimed peak at 1, 2 or 12
  semitones inside the note's perfect window, and it counts as real
  only when the model's note posterior of that rival also holds after
  its attack. Held-out list, settings from the development list:

  | Rule | Right accepted | Wrong rejected: semitone / tone / octave / added |
  |---|---|---|
  | `fair` (before) | 98.3 % | 78.3 / 74.9 / 47.1 / 69.5 |
  | paired, hand rule, floor 99 % | 98.4 % | 86.5 / 84.6 / 59.8 / 67.6 |
  | paired, hand rule, floor 99.5 % | 99.5 % | 76.9 / 75.6 / 55.1 / 31.6 |
  | paired, boosted trees on ten features, floor 99.5 % | 98.4 % | 92.8 / 92.1 / 71.9 / 72.6 |
  | paired, boosted trees, floor 98 % | 96.0 % | 97.0 / 96.4 / 82.2 / 88.5 |

  Hand rule (floor 99 %): a rival is real when its peak u >= 0.3, its
  note posterior after the attack un >= 0.15 and u >= s + 0.3. Accept
  when the written note's highest note posterior over its written
  length (at most 1 s) is at least 0.13 and no real rival sustains 0.15
  above it, or when s >= 0.25 and there is no real rival.

  - **Pairing is the lever.** Same model, same audio: 8 to 17 points
    more wrong notes rejected at the same floor. The fitted model shows
    the features hold more than the hand rule takes (the trees are a
    ceiling to aim at, not a rule to ship: they are not explainable and
    lose 1.1 points of right notes from one list to the other).
  - **Two different refusals.** "Another pitch was played" is cheap for
    right notes: it needs positive evidence. "Nothing was played" is the
    costly one: it can only rest on weak support, and that is where
    right low notes are lost. They must stay two facts in libkan
    (wrong pitch, no attack), each with its own bar.
  - The model's attack output fires on octaves of a right note (an
    unclaimed peak with a median of 0.45 next to right notes). Its note
    output does not (median 0.17). A rival needs both.
  - What is left: right notes below midi 48 (94 % accepted against
    99.7 % in the middle), wrong octaves in the middle register (55 %
    pass), and a written note that was not played at all while other
    notes sound (a third pass).
  - Next: the paired rule as a bench path (`pair`), with the two
    refusals reported apart, then features for what is left (octave:
    the rival's and the note's sustain compared over the written
    length; low register: a longer span after the attack).

- 2026-11-16, package 4d (merged): path `pair` in the bench
  (`truth_path_pair.hpp`). The architect rebuilt, ran ctest (3 of 3)
  and tabulated the builder's runs. Outputs (local):
  `truth-bench/wp4d-pair/`. The held-out row reproduces the study.

  | `pair`, held-out (development) | Accepted | Refused as wrong pitch | Refused as nothing played |
  |---|---|---|---|
  | right notes | 98.4 % (99.2) | 0.2 % | 1.4 % |
  | wrong semitone | 13.5 % (11.4) | 72.0 % | 14.5 % |
  | wrong tone | 15.3 % (14.9) | 70.2 % | 14.5 % |
  | wrong octave | 40.0 % (34.7) | 51.7 % | 8.3 % |
  | written, not played | 32.4 % (36.0) | 9.1 % | 58.6 % |
  | right, score late by 120 ms | 98.0 % (99.0) | 0.3 % | 1.7 % |
  | right, jitter 60 ms | 98.4 % (99.2) | 0.2 % | 1.4 % |
  | right, others left out | 97.9 % (99.0) | 0.9 % | 1.2 % |
  | right, shifted 20 cents | 97.9 % (98.7) | 0.3 % | 1.7 % |
  | right, shifted 40 cents | 94.8 % (97.1) | 0.1 % | 5.1 % |

  - **A right note is almost never told it was another pitch** (0.2 %).
    What is lost is lost as "nothing played". So the wrong pitch bar can
    be tightened further without cost, and "nothing played" is the fact
    to treat with doubt (not measured) when support is merely weak.
  - Attack time of accepted right notes: median +2.4 ms, 80 % within
    50 ms, 95 % within 185 ms. The tail is a wrong peak claimed (the
    strongest in the meh window, not the nearest to the score time).
    The summit asks 95 % in the perfect window: the claim must weigh
    closeness, to try next.
  - More witnesses tried on the dumps (longer sustain, contour, octave
    neighbours, in boosted trees): octave rejection stays at 72 to
    77 % at this floor. A bar that depends on register and written
    length lifts "written, not played" to 84 to 87 %: the model's
    resting level and reach differ by register, which is calibration
    data of the evidence source, not a special case.

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
