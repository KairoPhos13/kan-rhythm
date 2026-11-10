# Measurement invariants: the balance of factors

**Purpose.** Every property below is *measured*, not assumed. The system's
fairness (right playing passes, wrong playing fails loudly) depends on the
balance among them. A change that alters any invariant must do so as a
**deliberate, analyzed decision**: re-run the listed evidence, state the new
tradeoff in this file, and update the affected tests. Silently shifting a
threshold "because a fixture looked better" has already broken recall once
(see I1). That is what this file exists to prevent.

**Scope.** libkan describes; the game judges. libkan never emits verdicts,
but its measurements are *load-bearing* for game judging: the rules a judge
must follow to stay fair are recorded here as the **judging contract**
(§3), so a future game feature cannot accidentally rely on a property the
measurements do not have.

---

## 1. Invariants

Each entry: **what** the property is, **why** (the measured evidence),
**what breaks** if it is violated, and the **re-check list**.

### I1 (Onset detector): documented ×3.2 jump rule

- **What.** The onset threshold is a ×3.2 short-term energy jump, tuned
  on the 28-attack hand-annotated real material (`libkan/tests/
  harmonic_probe_notes.hpp`).
- **Why.** A "stricter" threshold looked cleaner on synthetic fixtures but
  missed **5 of 8 real attacks**; the documented rule finds **8 of 8**.
  Synthetic fixtures flatter thresholds; real playing does not.
- **Breaks.** Any threshold change without re-running the real-attack
  recall check silently trades recall for cleanliness.
- **Re-check.** `libkan_harmonic_probe --dump` on the real material; recall
  must stay 8/8 on the annotated attacks. `libkan_score_error_test` must
  stay green (the "late note" and "extra note" signatures depend on the
  detector).

### I2: One estimate per row; unmeasured is `-`, never a number

- **What.** Each JSONL row carries at most one value per field; fields that
  cannot be measured honestly are `-`/null (e.g. pitch on MIX/HOP rows).
- **Why.** Phase 0 fixed rows that emitted two competing pitch estimates;
  downstream consumers (and a player being judged) cannot resolve which
  one to trust.
- **Breaks.** Emitting "best guess" values for unmeasurable rows invents
  confidence and can convict an innocent take.
- **Re-check.** `libkan_note_stream_test` (hopping-note fixture) and the
  JSONL schema doc in `note-description-stream.md` §5.2.

### I3: Final pitch = amplitude-weighted CENTER of the trajectory

- **What.** The final pitch fact is the amplitude-weighted mean of the
  fitted pitch over the note's life, the vibrato center, not the initial
  value. Gate: mean fit similarity ≥ 0.80 AND presence ≥ 0.05.
- **Why.** Vibrato and attack transients make the initial value the worst
  estimator; the center halves the cents error on real takes (§2.4 of
  `note-description-stream.md`).
- **Breaks.** Using first-value or max-amplitude pitch re-opens the ±53 c
  errors the center fixed; dropping the gate lets noise report a pitch.
- **Re-check.** `libkan_note_stream_test` vibrato-center fixture;
  `--verify-score` pitch stats on `apps/kan_demo/examples/
  hallelujah-score-1.score` (mean ≈ 8 c, max ≈ 31 c).

### I4: No measurement window crosses a strike boundary

- **What.** Evidence after a strike belongs to that strike's group; windows
  never straddle an attack.
- **Why.** Spill-over was the source of phantom records (the blind
  generator's 341 ms window leaking into the next attack produced records
  like C#3/F#4 at strike 1 on the real Hallelujah take).
- **Breaks.** Cross-strike windows fabricate notes and smear durations.
- **Re-check.** `libkan_note_stream_test` fixture 11 and the sustained-under-
  new-attack fixture; on real data, record counts per strike must stay
  bounded by the instrument cap.

### I5: Re-strike vs extension decided by level, at the strike

- **What.** A level jump ≥ 1.5× the hold's current level at a strike = new
  record (`RESTRIKE`); no jump = extension of the existing record. Decided
  per note at probe fit, for repeated pitches in consecutive strikes.
- **Why.** Pitch identity alone cannot tell a repeated note from a held one;
  energy can (validated on fixture 11 and real repeats).
- **Breaks.** Timing-based heuristics merge repeated notes or split held
  ones; the game would see phantom misses.
- **One record at a time on mono profiles.** When `max_simultaneous_notes`
  is 1, any record that opens (strike, deferred re-strike, transition)
  ends every other live record at its onset. Before this (2026-10-15), a
  re-strike closed only the first live record. A second record kept its
  pending legato shift, and both opened the same transition: identical
  twin notes, and parallel record chains that never closed (a real
  ocarina take had 617 notes, 442 overlapping and 56 running to the end
  of the file; after the fix, 353 notes and none of either).
- **Articulation by level (profile `rearticulates_by_level`, winds).**
  Winds articulate with the air, often with no attack transient for the
  onset detector (I1, unchanged). On the 10 ms level line:
  - a dip below the release level (`end_drop_db`) that comes back within
    90 ms is a tongued note: a new record at the bottom of the dip;
  - a longer low is the release: the note ends where the level dropped;
  - a rise well above the room level out of silence opens a note at the
    start of the rise.

  A detector strike within 60 ms of a level strike is the same attack.
  Slurs use `reading_interval_ms` (wind: 20) and `transition_hold_ms`
  (wind: 80; was 250 for every profile), dated at the reading window's
  centre.

  Synthetic wind phrases (fixture 14), before → after:
  - slurred steps 21 % → 98 %;
  - tongued repeats 0 % → 98 %;
  - tongued changes 12 % → 100 %;
  - after a breath 54 % → 100 %;
  - extra notes 88 → 0 (330 written);
  - median lateness +32 → +3 ms.

  Real ocarina take against the owner's corrected score: F1 (±50 ms +
  pitch) 37.6 % → 57.0 %. Transcription is 25.2 s → 19.4 s for 220 s of
  audio (I11).
- **A release tail is not an attack (mono profiles that rearticulate,
  2026-10-22).** A real ocarina take had short repeated C6 (held ~170 ms
  every ~500 ms). A faint reflection in each release tail, ~-20 dB under
  the note, jumped the transient detector against the near silence before
  it, or rose 4× over the room level once the record had closed. Either
  way a note opened ~200 ms before the next one, and the game's judge took
  it for that note (Meh/Good for notes played on time).

  Now a sound far under the last note's level (bucket RMS below 0.2 of
  it; that level fades with a 500 ms time constant) opens a note only
  when it rises to that share. A quiet transient waits up to 60 ms for
  the rise and then opens at its own time, so a soft attack keeps its
  onset. Out of silence, the rise must also reach that share.

  Results:
  - fixture 16: 8 attacks found, 0 extra (was 8 extra);
  - fixture 14: unchanged;
  - the owner's take: onsets in release tails 28 → 6, and the 111
    onsets that rise to a note are the same.
- **A tongued note is read where it speaks (mono profiles that
  rearticulate, 2026-10-23).** A real ocarina take: an F5 tongued into a
  C5. The level came back at -10 dB with the F still in it, and the record
  opened at the dip's bottom. The C sounded ~30 ms later, but the
  provisional pitch read the window just after the onset: the F (77.39).
  The final pitch, the centre of the whole note, came only when the 738 ms
  note ended, and the game judged the note not heard.

  Now a note out of a dip, or out of a quiet transient, speaks once its
  bucket RMS reaches half the level of the note before (`kSpeakShare`).
  A note that stays softer speaks after 100 ms (`kSpeakPatienceMs`). Its
  provisional pitch, readings and fit windows start there; the HIT keeps
  the onset time.

  Results:
  - fixture 17: one note after the dip, provisional C (was the F, 76.8-77.0,
    then a second record for the C);
  - that C in the owner's take: provisional 73.18 (C5 with its tuning);
  - the whole take (469 attacks):
    - provisional ≠ final 31 → 20;
    - provisionals over a semitone off the score's note 70 → 49;
    - provisional latency median 66 → 71 ms, p90 81 → 125 ms (soft
      notes that wait out the patience).
- **A tongue cutting a note is no attack (mono profiles that
  rearticulate, 2026-10-23).** A real ocarina take of repeated D5s: the
  tongue stops each note, the level falls to about -15 dB, and the cut
  leaves a click of ~20 ms at about -9 dB before the silence. The click came
  back above the release level (`end_drop_db`), so it was taken for a
  tongued note, opened at the cut ~150 ms before the next attack. It carried
  the same pitch, and the judge took it for the next note: an early good
  instead of a perfect.

  Now a level back from a dip opens a note only once it reaches half the
  note's level (`kSpeakShare`) or holds for 40 ms (`kDipBackHoldMs`). A
  level that falls back before that is the cut, and the release goes on.

  Results:
  - fixture 18: no attack at the cut for clicks of 35-40 % lasting
    20-30 ms (the old rule opened one each time); a soft tongued note at
    35 % that holds still opens at its dip;
  - the owner's take (76 attacks): 10 attacks gone, each followed by
    silence (about -47 dB) and the real attack 140-200 ms later; 2 real
    attacks moved onto their rise.
- **Re-check.** `libkan_note_stream_test` re-strike fixture, fixture 13
  (wind phrases: no record open when the next opens, no twins) and
  fixture 14 (wind articulation recall and timing), fixture 16 (release
  tails), fixture 17 (a tongued note's pitch) and fixture 18 (a tongue's
  cut).

### I6: Similarity alone is not evidence of correctness; cents must gate

- **What.** A written pitch can absorb energy it does not produce: in the
  counterexample suite a wrong-pitch note kept **sim 0.91** while the fit
  bent **−56 c** toward the sounding note.
- **Why.** Measured in `libkan_score_error_test` (wrong-pitch case) and on
  the real take (corrupted score: sim 0.52 there, but the synthetic case
  shows sim can stay high).
- **Breaks.** A judge that passes on sim ≥ threshold alone will pass wrong
  notes that are merely detuned-toward; a judge with a wide band (±60 c)
  half-swallows a semitone error (−56 c measured).
- **Judging rule.** Gate on `|cents|` with a band of **±40-50 c** (see §3);
  use sim as a presence check, not a correctness check.

### I7: Amplitude can be leased; presence/sim say whether a note sounds

- **What.** A note that was never played can report a non-trivial fitted
  amplitude from neighbor leakage (0.12 measured), while its sim collapses
  (0.06).
- **Why.** Measured in `libkan_score_error_test` (missing-note case).
- **Breaks.** Amplitude-threshold judges convict silence or acquit ghosts.
- **Judging rule.** "Played or not" is decided by sim/presence; amplitude is
  a loudness fact only.

### I8: Duration measurements carry a documented +~200 ms release bias

- **What.** Measured durations exceed written ones by ≈ +200 ms (mean;
  max ≈ 302 ms) on piano: the release decay after key-off counts as
  sounding under the −20 dB end rule.
- **Why.** Measured over the 74-note Hallelujah score (`test_results/
  validation/07-hallelujah-score-1.report.txt`).
- **Breaks.** Judging duration with a tight tolerance on raw measured
  duration convicts correct playing; "fixing" the end rule without the
  release-point analysis changes every historical measurement.
- **Judging rule.** Compare `measured − written` against the bias (≈ 200 ms)
  plus tolerance; a duration error must exceed the bias to be visible
  (`libkan_score_error_test` wrong-duration case asserts exactly this).

### I9: Live and offline paths are byte-identical

- **What.** The same audio through the live and offline paths produces the
  same events. GPU is deliberately excluded (it would break this).
- **Why.** A judge that behaves differently live vs in replay is not fair;
  bug reports become unreproducible.
- **Breaks.** Any device-dependent fast path, threading race, or
  nondeterministic reduction (this is also why the analysis budget is
  single-threaded per take; see I11).
- **Re-check.** `libkan_note_stream_test` live/offline equality fixture.

### I10: Instruments are data, not code

- **What.** Instrument behavior (max_simultaneous_notes, mono_poly_threshold,
  accepts_legato_boundaries, intonation_band_cents) lives in
  `InstrumentProfile` (pack data). libkan's hard cap is 10 notes.
- **Why.** A flute or melodica must be a new profile, not new code paths;
  otherwise every instrument change risks re-breaking I1-I9.
- **Breaks.** Hardcoding instrument assumptions in the analysis invalidates
  the whole chain for the next instrument.
- **Re-check.** Adding an instrument must not touch `libkan/` at all.

### I11 (Resource budget): ≤ 2 analysis threads, ≤ 4 cores total

- **What.** DSP analysis ≤ 2 worker threads; total process ≤ 4 cores on an
  ordinary machine. The measured budget is met single-threaded (Hallelujah
  254 s take = 44 s CPU ≈ 0.17× real time after AVX2 Gram + cache-order
  fixes).
- **Why.** The game must run while being played (≤ ~500 ms total latency
  budget for final facts) alongside rendering/audio.
- **Breaks.** Worker pools or GPU "optimizations" break I9 or steal the
  game's cores.
- **Re-check.** `libkan_harmonic_probe --bench`; wall time on the 254 s
  take must not regress beyond ~1.5×.

### I12: Real-time safety of the event path

- **What.** The audio callback only pushes fixed-size POD events through the
  lock-free ring buffer: no allocation, no locking, no I/O on that path.
- **Why.** Audio glitches during judging would be worse than any scoring
  error (repo rule 3).
- **Breaks.** Any convenience (logging, strings, vectors) added to the
  callback path.
- **Re-check.** Code review of the callback path on every change to
  `note_stream.hpp`; `libkan_note_stream_test` stream tests.

---

## 2. Accepted tradeoffs (decided, not accidental)

These are *shallow* gaps we accept today. Each one is closed by a named
future option, never by quietly weakening an invariant above.

### T1: A wrong note can pass as "detuned" (accepted)

A semitone error bent to −56 c looks like intonation under a ±60 c band.
We accept that error of *characterization* (wrong note ≈ badly detuned) as
a tradeoff: it never acquits truly wrong pitches (a semitone cannot be
covered by a tight band), and harshness would punish the very real detune
of acoustic takes (measured −25 c mean, up to −53 c).

- **Closing option (future, game-side):** a per-player **intonation
  profile**. The game accumulates the per-note `cents` facts libkan
  already emits (pitch, cents, amplitude per note event), computes the
  player's bias per pitch region over judged-good play, and judges
  bias-corrected with a tight band (±30-40 c). This needs **no new libkan
  code**: the contract is that the stream always carries per-note cents and
  amplitude with the semantics of I3 (center of trajectory).
- **If someone tightens the band at game level**, they must re-check the
  detune stats on real takes (I3 re-check) so honest detune is not
  convicted.

### T2: Extra pitch inside a written chord is weakly visible (accepted)

A player adding a voice to a chord the score already contains shows only a
mild sim drop (0.88-0.90 vs 0.95-0.97 measured). It usually does not sound
wrong and does not break immersion; convicting it from blind evidence would
require the over-proposal machinery we deliberately do not want.

- **Closing option (future, game-side):** chart-supplied hypotheses per
  strike make it a set comparison instead of a blind fit (the game knows
  exactly which notes should sound at each strike).
- **Until then:** the `extra strike (played, not written)` report marks
  strikes the score cannot account for; the sim drop is visible in the
  report for anyone who looks.

### T3: Accelerators (GPU) stay out of the validated core (decided)

Large-scale evaluation (e.g. MAESTRO, ~200 h) tempts GPU acceleration: the
workload is millions of small independent fits. We keep the analysis core
CPU-only anyway, because **the benchmark must validate exactly the code the
game runs** (measuring a GPU numerical path while shipping a CPU one would
validate the wrong product) and GPU floating point is not byte-stable
across drivers (it would violate I9).

- **Scaling levers that do not break the balance:** offline `--jobs` (the
  ≤2 analysis threads of I11 bound the *live* path, not offline packs;
  user decision: 4-6 threads OK offline), coarser `--fit-step` for triage
  runs, and deterministic `--limit`/`--offset` paging over the dataset.
  Measured: full MAESTRO ≈ 34 CPU-h ≈ 3 h wall at 12 jobs.
- **Closing option (future):** a GPU *batch* backend may be added to the
  offline benchmark only, as an optional accelerator behind an equivalence
  test against CPU on a reference subset (|Δcents| ≤ 1 c, |Δsim| ≤ 0.01),
  and only CPU runs may source published measurements until equivalence is
  demonstrated and recorded here.

---

## 3. Judging contract (for the game; recommendations, not libkan code)

A game judge built on these measurements stays fair if it follows:

Game-side implementation requirements for all of this (gates G1-G5,
compute scheduling, player calibration, and the "must never" list) live in
[`game-judging-guide.md`](game-judging-guide.md).

1. **Presence/played-or-not:** sim/presence gates (sim ≥ 0.60 found;
   ≈ 0 means not played). Never amplitude (I7).
2. **Pitch correctness:** gate on `|cents|`, band **±40-50 c** (I6);
   optionally bias-corrected per player (T1). sim is a presence check
   only.
3. **Timing:** use the strike time facts; for score-anchored verification
   the `t_peak` offset is the visible late/early signature.
4. **Duration:** compare against the release bias (I8): error must exceed
   ≈ +200 ms baseline (piano; other instruments may need their own
   calibration recorded in the pack).
5. **Never re-derive** a verdict from raw audio in the game: consume the
   facts; if a fact is `-`/unresolved, the judge's only fair action is to
   not judge that dimension.

---

## 4. How to change an invariant (the analyzed-decision checklist)

1. State the new evidence in a commit message with numbers (before/after on
   the same inputs: real-attack recall, `--verify-score` stats on
   `hallelujah-score-1.score`, `--bench` wall time).
2. Run the full suite: `ctest --test-dir build` (19 tests), including
   `libkan_score_error_test`. The counterexample signatures are the
   fairness floor.
3. Update the affected invariant in this file (what/why/what-breaks) and
   any cross-referenced doc.
4. If a tradeoff shifts (T1/T2 or a new one), record it in §2 with its
   closing option.

---

*Cross-references:* overall status and readiness `validation-status.md`;
design of record `note-description-stream.md` (§7
decisions); evidence `libkan/tests/harmonic_probe_notes.hpp`;
counterexamples `libkan/tests/score_error_test.cpp`; budget `docs/
architecture/platform-architecture.md`.
