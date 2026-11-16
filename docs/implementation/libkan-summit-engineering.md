# libkan summit: engineering guide

How the polyphonic path is designed, how to build it, and the rules of
the work. Written so that any engineer or agent can continue without the
people who started it. Companions:

- what and why: `docs/architecture/polyphonic-live-measurement.md`;
- status, packages, findings: `docs/implementation/libkan-summit-plan.md`;
- briefs, one per work package: `docs/implementation/libkan-summit-briefs/`.

Where this guide states a hypothesis, it says so. Everything else is a
rule.

## 1. System design

### 1.1 Data flow

```
audio device or file
  -> game worker thread "kan live describer" (LiveSession.run)
     -> kan_forge_live_process, 10 ms blocks        [C ABI, game/forge/native]
        -> one voice:  NoteDescriber                 [libkan, unchanged role]
        -> many keys:  ChartMeasurer                 [libkan, new]
     <- events (fixed-size structs)
  -> game queue -> KanPlayState.takeFacts
     -> policy layer (facts to grades) -> judge, health, mods
     -> visuals (keys lighting, trail)
```

The audio callback only copies samples. All measuring runs on the worker
thread. The game thread only reads events.

### 1.2 Who owns what

| Concern | Owner | Why |
|---|---|---|
| Which sound belongs to which written note (pairing) | libkan, `ChartMeasurer` | it is the hard part and needs the signal |
| Attack time, pitch against rivals, level, close | libkan | measurements |
| Windows, grades, mods, health, streaks | policy layer | choices, not measurements |
| Where the policy layer lives | the game today; an optional libkan module later (plan, section 5) | a pure function from facts to a grade; the measurer never depends on it |
| Instrument behaviour | `InstrumentProfile` (pack data) | no code per instrument |

### 1.3 ChartMeasurer, inside

Five parts with narrow seams, so each can be built and tested alone:

1. **Chart store.** Every written note: id, start, end, pitch, search
   window. Set once (setup path, may allocate), sorted, then read only.
   All later memory is sized here from the chart's own peak density.
2. **Clock map.** Segments `(stream_ms, chart_ms, rate)` plus a hold
   flag for "wait for the player". libkan's own times are always stream
   time: milliseconds of samples received. Chart times are converted at
   the seam, nowhere else.
3. **Evidence.** One interface, several sources. Given a pitch and a
   stream time it answers two questions: how strong is an attack of this
   pitch here (the attack curve), and how strongly does this pitch sound
   here (the level). Sources: harmonic (libkan's joint fit) and learned
   (Basic Pitch posteriors). A source may say "not ready yet" with the
   time it will be.
4. **Lanes.** One state machine per written note:

   ```
   Waiting -> Provisional -> Final -> Sounding -> Closed
                         \-> NoAttack
                         \-> NotMeasured
   ```

   - *Waiting*: the search window is not open yet.
   - *Provisional*: a likely attack was seen. Emitted fast so a key can
     light. May be withdrawn by the final fact.
   - *Final*: attack time fixed, pitch confirmed against rivals from the
     tail. Carries support and margin.
   - *NoAttack*: the window closed and no pitch of the set attacked.
   - *NotMeasured*: evidence too close to call, or effort was shed.
   - *Sounding*, *Closed*: level while it rings, then the end (flagged
     when the end faded and was not observed).

   Each transition has a deadline in stream time, from profile data: a
   low struck note waits longer for its tail than a high one.
5. **Scheduler.** Work is cut in units (one fit, one network chunk),
   each with a deadline and a cost estimate. The chart gives the density
   ahead, so the plan for a passage exists before it arrives.

### 1.4 Rules of the design

- **Determinism (I9).** A fact depends on the samples and the chart,
  never on when a function was called. Windows end on a fixed sample
  grid. Shedding follows the planned cost of the chart's density, which
  is the same on every run. A wall-clock valve may exist for a machine
  that falls behind, and it can only turn facts into NotMeasured, with a
  flag that says so.
- **Unmeasured is never wrong (I2).** No number is invented. A fact that
  cannot be measured honestly says so.
- **Fair to what is played right, first (owner, 2026-11-16).** Of the
  two errors, penalising a right note is the worse one: a player who
  plays well and is told otherwise loses the joy of it. Settings are
  chosen for right notes accepted first, and wrong notes rejected is
  reported as it falls. In doubt a fact is accepted or not measured,
  never a miss. The negative controls are still always reported.
- **Pitch is relative to the instrument (owner, 2026-11-16).** People
  play common instruments, and a wind drifts with air pressure. libkan
  keeps a running estimate of the instrument's tuning and every pitch
  test (rival search, cents tolerance, partial search range) is taken
  from that estimate, never from standard pitch. The game did this in
  its judge (`game/core/Play/TuningTracker.cs`: the median deviation of
  recent readings, readings further than 0.7 semitones left out, at most
  80 cents). That logic moves into libkan with the pairing, next to
  `DetuneCalibrator` (`libkan/include/libkan/calibration.hpp`).
- **Settling is not an error (owner, 2026-11-16).** A blown note may
  take a short time to reach its pitch. Pitch is read after the settle
  time of the profile. The settle is reported, never refused.
- **Facts are deviations (owner, 2026-11-16).** A final fact carries
  the time deviation in ms and the pitch deviation in cents, relative
  to the player's running offset and the instrument's running tuning,
  so the policy can grade perfect, ok, meh and fail as the game does.
- **A refusal says how sure it is (owner, 2026-11-16).** `clear` on
  positive evidence, `grey` on weak support. The hidden streak
  protection of the policy layer spends its budget only on grey ones
  (plan, section 5b).
- **Time and pitch are separate measurements.** Time from the attack with
  short windows. Pitch from the tail (struck) or the hold (blown) with
  long windows.
- **An attack is a rise.** Level alone is no attack: under the pedal a
  rival may ring for seconds.
- **Real-time safety.** After the chart is set: no allocation, no lock,
  no I/O on the worker's measuring path. Events are fixed-size structs
  through the lock-free stream.
- **The C ABI is a contract.** The event struct keeps its layout. New
  event kinds are added at the end. A field's meaning never changes. A
  new call never changes what an old call does.
- **One voice keeps its path.** A mono profile does not enter
  `ChartMeasurer`. Its event dump is compared after every change; a
  difference must be explained and measured (it may be a fix).
- **libkan stays self-contained.** Nothing in `libkan/` includes from
  outside it.

### 1.5 Profile data the polyphonic path needs (to add when measured)

Envelope family (struck or held). Tail window for pitch by register.
The partial band used for attack time. Inharmonicity by register (piano
partials sit above whole multiples, more so high up). The rival
intervals (default 1, 2, 12 semitones). Deadlines for provisional and
final facts. Each value enters the profile with the measurement that
set it, recorded in the plan's findings.

## 2. Implementation practices

### 2.1 The order of any new idea

1. **Bench path first.** Write the idea as a path in the truth bench, in
   `game/forge/native/`, using libkan's existing pieces. No library
   change, no API change.
2. **Features file.** The path writes its raw numbers per score note
   (supports, margins, times) to a CSV. Rules are designed from that
   file, not by rerunning the audio.
3. **Tune on the development list. Report on the held-out list.** Never
   tune on the held-out list. If it was looked at to choose a setting,
   say so next to the number.
4. **Keep or drop.** A change stays only if the held-out table is
   clearly better and no measure is worse. Dropped ideas are written in
   the plan's findings with their numbers, so nobody tries them twice.
5. **Then the library.** Move the proven logic into libkan with offline
   tests, behind the seams of section 1.3.

### 2.2 Tests

- Every DSP change has an offline test with known input and expected
  output (repo rule 5). Synthetic signals with exact truth.
- A failure found on real audio becomes a small synthetic fixture that
  shows the same failure, then the fix. This is how the one-voice path
  was hardened (`docs/architecture/measurement-invariants.md`).
- The truth bench's selftest holds a floor for the new path. Raise the
  floor when results improve, never lower it without a written reason.
- No recognition number is reported without its negative controls.
- CPU numbers: pinned with `taskset`, repeated at least three times,
  reported with their spread. Another workload may share the machine.

### 2.3 Code

- C++20, `-Wall -Wextra`, no new warnings. Header-only core in
  `libkan/include/libkan/`; compiled parts in `libkan/src/`.
- No instrument, register or person special-cased in code. If a value
  differs by instrument it is profile data.
- Never remove a capability to gain speed. Make it run better.
- Comments say why, in short plain English. No clause-separating dashes.

### 2.4 Review checklist (the architect, for every package)

1. Read the whole diff. Does it stay inside its lane?
2. Can any path see the truth? Only `oracle` may.
3. Build bounded and run ctest yourself.
4. Run the bench yourself. Do the builder's numbers reproduce?
5. Do the controls move the way physics says? A result that is too good
   is checked first for leaked truth.
6. One-voice dump compared.
7. Authors of every commit: `KairoPhos <kairophos@proton.me>` only.
8. Merge with `git merge --no-ff`, update the plan's table and findings.

## 3. Work policies

- **Worktrees.** One package, one worktree, one branch
  (`docs/agent-workflow.md`). Only the main folder edits `libkan/` on
  `main`: a package that changes libkan is merged by the architect after
  review.
- **Bounded builds.** `nice cmake --build <dir> -j 6`. `JOBS=6` for
  `make`. `-maxcpucount:4` for dotnet. Never a bare `-j`. No full clean
  rebuild without asking the owner: one froze this machine.
- **The builder.** Launch:
  `opencode2 run --auto --model opencode-go/mimo-v2.6-pro "$(cat brief.md)" </dev/null`
  in the background, from the package's worktree. Continue with
  `-s ses_XXX`. Never relaunch a busy session. The architect takes a
  problem over only after about three failed attempts.
- **Briefs.** One per package, kept in the briefs folder. Each names:
  files to read, code to reuse, exact deliverables and tests, what not to
  touch, the bounded build commands, the runs to do, and the final
  summary wanted (short, deep, with numbers).
- **Data.** MAESTRO is CC BY-NC-SA 4.0. Recordings, derived levels and
  every output stay under `tools/dataset_bench/data/` (ignored by git).
  Never committed, never uploaded. Manifests of file names are fine.
- **The game.** Never launched on the owner's screen. Isolated instance
  only (`game/tools/bench-play.sh`), input from `KAN_LIVE_INPUT`, never
  a microphone.
- **Identity.** Commits as `KairoPhos <kairophos@proton.me>`. No other
  name, handle or email anywhere. No push, no tag, no deploy: the owner
  pushes.
- **Writing things down.** A package is not done until the plan's table
  and findings are updated. Decisions carry a date and who made them.

## 4. Harmonic evidence: where to start (hypotheses, to be measured)

Package 3 of the plan. These are the architect's starting points, not
results.

- **Attack time.** For a pitch, sum the energy of its partials above
  about 500 Hz in short windows (about 21 ms, every 10 ms). The attack
  curve is the rise of that sum over the frames before. Upper partials
  answer in a few milliseconds even for a low note. Expect an error well
  inside 50 ms. To test: is the rise specific enough to the pitch, or is
  a broadband onset detector plus the tail's pitch check enough?
- **Pitch from the tail.** After a likely attack, one long window (up to
  8192 samples, 170 ms) starting about 30 ms after it. A joint fit of
  the written notes alive, plus this note and its rivals, in groups of
  at most 10 (`HarmonicChordValidator`, capacity already there). The
  written note wins when its fitted level rose against the window before
  the attack and stands above each rival's rise.
- **Octaves.** The upper note's partials are all partials of the lower.
  Tell them apart by the lower note's odd partials: written low, played
  high leaves them empty; written high, played low fills them.
- **Low semitones.** Compare at the partials where the two notes sit
  furthest apart, with the long window. Fundamentals alone will fail.
- **Inharmonicity.** The fit models partials as whole multiples. Piano
  partials are stretched. With six partials the error is small in the
  middle and may matter at the top. Measure by register before adding a
  stretch value to the profile.
- **Cost.** A fit costs about atoms squared times window length. Rivals
  add atoms. Hypothesis: fit rivals only at likely attacks, never on the
  10 ms grid.
- **Fusion with the learned evidence.** Start with the simplest rule that
  the features files support (for example: accept when both agree, not
  measured when they disagree), and only then try weights.
