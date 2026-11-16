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
| 1 | Truth bench, selftest, manifests, baseline of `blind`, `expect`, `core` | | B | building (`agent/truth-bench`) |
| 2 | Learned evidence in the bench: a `bp` path on offline Basic Pitch posteriors (written note against rivals, attack time), then the same with emulated look-ahead of 50 to 500 ms | 1 | B | next |
| 3 | Harmonic evidence in the bench: attack curve from short windows, pitch from the tail with long windows, rivals in the joint fit. Prototype as a bench path first | 1 | A then B | open |
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
