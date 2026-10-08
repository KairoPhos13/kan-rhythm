# Test log — dataset benchmarking (MAESTRO and successors)

Chronological log of large-scale evaluation batches: what was run, what
passed, what broke, and what was done about it. Every entry references the
batch outputs under `runs/` (gitignored) and the invariants in
[`../../docs/architecture/measurement-invariants.md`](../../docs/architecture/measurement-invariants.md).

Workflow rule (user decision): **run in batches, find and fix issues
between batches** — never the whole dataset blind. Dataset: MAESTRO v3.0.0
(Google Magenta, CC BY-NC-SA 4.0), extracted per year-folder under
`data/` (disk cannot hold the full 129 GB at once).

Status legend: **PASS** measured as expected · **ISSUE** a defect or
suspicious behavior, tracked until resolved · **NOTE** an observation that
does not block.

---

## Batch 01 — smoke on real data (2004 folder, 5 files)

- **Date:** 2026-10-07
- **Scope:** first 5 entries with audio (`--limit 5`), ~52.6 min audio,
  37,241 ground-truth notes (real Disklavier performances)
- **Command:** `kan_dataset_bench --maestro data/maestro-v3.0.0 --limit 5
  --jobs 5 --out runs/batch01`
- **Results:** 36,733 / 37,241 found (**98.6%**, sim ≥ 0.60); |cents| mean
  **6.7 c**, max 60.0 c; duration err mean **+207 ms**; 29 extra strikes;
  throughput **15.2× real time** on 5 jobs (3,155 s audio in 207 s wall).

### Good passes

- **PASS-1 — recall at scale.** 98.6% of 37k notes verify against
  millisecond ground truth; sim p5 = 0.80, median 0.96.
- **PASS-2 — I3 pitch center holds at scale.** mean |cents| 6.7 c is
  consistent with the 8.0 c measured on the Hallelujah take; the
  amplitude-weighted center is the right final-pitch fact.
- **PASS-3 — I8 release bias confirmed at scale.** +207 ms mean duration
  error on real Disklavier audio matches the documented ~+200 ms piano
  release bias almost exactly.
- **PASS-4 — extra-strike noise is rare.** 29 unmatched strikes across
  37,241 notes (0.08%).
- **PASS-5 — throughput better than budgeted.** 15.2× real time on 5 jobs
  (projection in README was ~0.17×/core for the blind stream; the
  score-anchored core is far cheaper). Full MAESTRO ≈ 13 h wall at 5 jobs,
  less at 12.

### Issues

- **ISS-1 — cents saturate at the ±60 c search cap in the deep bass.**
  228 notes (0.62%) report |cents| > 45, and the top offenders are all
  exactly ±60.0 (the search boundary) with midi ≤ 43 (e.g. midi 26/31/41).
  Deep piano bass is inharmonic and fundamental-weak, so the fit drifts to
  the range edge and the number it reports is the *cap*, not a measurement.
  Violates the error discipline (I2: never report an estimate that is
  actually "ran out of range").
  *Plan:* flag saturated estimates (report `-` or a `saturated` bit) and/or
  widen the search range for midi < ~45 and re-measure. Do not let a game
  judge act on a saturated number.
  **RESOLVED (this batch round):** the search range is now pack data on
  `InstrumentProfile` (`pitch_search_cents`, `pitch_search_cents_low`,
  `low_register_break_midi` — per-register bands per instrument, no code
  per instrument), and any estimate landing at its register's cap is
  flagged `saturated` and excluded from pitch facts (I2). The fit explores
  the wider range; trust stays per band. Asserted in `libkan_score_error_test`
  (wrong-pitch case). To be re-verified at scale in batch 03.
  **FOLLOW-UP (user decision):** the trust band is now a *smooth linear
  curve* with register (`pitch_search_cents_at(midi)`, anchored at
  (33, 120 c) and (57, 60 c) for keyboard), not a hard breakpoint --
  inharmonicity grows continuously toward the bass, so the band must too.
  Flat bands for instruments without a bass problem (wind). Asserted in
  `libkan_score_error_test` (band-interpolation block).
- **ISS-2 — long sustained notes truncate early (negative duration error).**
  The 31 rows with |dur_err| > 600 ms (0.08%) are all *negative* and all
  long notes (written 2.7–6.2 s): e.g. written 6210 ms measured 3379 ms.
  Cause: the end rule "fit level < 10% of peak" fires early on decaying
  long notes whose level dips below 10% of their attack peak.
  *Plan:* decay-aware end rule (compare against the local plateau or follow
  the decay slope to key-off), re-check the I8 bias afterward — the end
  rule change alters historical measurements (checklist in
  measurement-invariants.md §4).
  **RESOLVED (this batch round):** release-based end rule — a note ends at
  its release, the terminal drop of `end_drop_db` below its own sustained
  (plateau) level. Envelope-shape agnostic: piano decays, winds swell, both
  end in a release (the user's wind concern is exactly why the rule is
  relative to the plateau, not the peak). `end_drop_db` is pack data
  (keyboard 12, plucked 18, wind/bowed 10). Re-check I8 in batch 03 — the
  bias number may shift with the new end rule.
- **ISS-3 — one file carries most misses.** 318 of the 508 not-found notes
  live in a single file (`SMF_05_R1_2004_01-..._Track02`, 18,947 notes).
  *Plan:* inspect that file in batch 02 analysis (miss clustering: soft
  passages? repeated notes? file-level gain?) before tuning anything.
  **DIAGNOSED (this batch round):** not an ensemble or foreign-instrument
  file — the metadata row says *Frédéric Chopin, "12 Etudes, Op. 25"*,
  solo piano (the whole CSV is solo-piano repertoire; no multi-instrument
  entries in v3.0.0). The real cause is **note density**: 18,947 notes in
  23.5 min = 13.4 notes/s; the alive-window rule (written dur + 250 ms)
  puts a **peak of 27 simultaneous candidates** and >10 (the
  `kMaxScoreNotes` fit cap) for 4.3% of the piece. Truncated candidates
  are never fitted → sim collapses (miss median sim 0.43) → scattered
  misses (median gap 197 ms). Virtuoso fast passages, not bad audio.
  *Plan:* when >10 notes are alive, fit in groups so every alive note is
  measured (each fit stays <= 10 candidates — the RT budget cap is about
  per-fit cost, not about refusing to measure). Until then, treat dense-
  passage misses as a known cap artifact, not a detection failure.

---

## Batch 03a — fixes verified on real data (same 5 files as batch 01)

- **Date:** 2026-10-07
- **Scope:** `--limit 5` (identical entries as batch 01, for direct
  comparison), 37,241 notes, all fixes applied: saturation flag (ISS-1),
  release-based end rule (ISS-2), smooth register bands, heavy cavalry
  (ISS-3), percentiles + per-register stats
- **Command:** `kan_dataset_bench --maestro data/maestro-v3.0.0 --limit 5
  --jobs 5 --out runs/batch03a`
- **Results:** found **98.8%** (was 98.6%); |cents| mean 8.1 c; duration
  err mean **+209 ms** (was +207 — the release rule does NOT shift I8 ✓);
  dur_err p50 210 / p95 246 / p99 249 ms (was max 2831 ms); **390 saturated
  estimates flagged and excluded** (1.1% of found); |cents| p50 4.0 /
  p95 31.2 / p99 53.7 c; per register: low p50 16.4 / p95 67.2 · mid 3.7 /
  25.3 · high 3.7 / 29.1 c.

### Good passes (fix verification)

- **PASS-6 — ISS-2 fixed at scale.** Duration error max dropped from
  2831 ms to **1051 ms** and the distribution is tight (p99 249 ms ≈ the
  release bias itself): long notes no longer truncate early.
- **PASS-7 — I8 stable under the new end rule.** +209 ms vs +207 ms: the
  release-based end rule preserves the documented bias (the §4 invariant
  checklist was followed; the number survives).
- **PASS-8 — ISS-1 fixed at scale.** 390 estimates now report "ran out of
  range" instead of a fake ±60 c number; all excluded from pitch facts.
  The max |cents| of 111.3 c is real exploration in the bass (wide band),
  not saturation masquerading as measurement.
- **PASS-9 — per-register honesty published.** Low register is honestly
  weaker (p95 67.2 c) than mid/high (p95 ~25-29 c) — the game-judging
  guide (docs/architecture/game-judging-guide.md) records the consequence:
  never convict a bass note with a treble-grade band.
- **NOTE-1 — heavy cavalry has a cost.** Throughput 6.96x real time (was
  15.2x) on 5 jobs: dense passages now spend 2 passes / 4 refine rounds.
  Effort where density demands it, exactly the agreed tradeoff.

### Mash counterexample (user question: "press random keys that include the right ones")

- **NOTE-2 — the note gate alone passes the cheat (measured).** Playing the
  correct chord mashed with 3 random keys verifies the written notes (sim
  stays ~0.85); sim does NOT drop — the extras' energy is *absorbed* into
  the written fits (amps inflate ~6x, cents bend -25 c). Score-error case 9
  keeps this honest.
- **PASS-10 — the purity fact catches it.** Per-strike unexplained energy
  (1 - sim0): 0.1% clean vs **6.1%** mash; the extra-pitch candidate search
  finds all 3 true keys among 13 candidates (total recall, noisy neighbors
  -> informational only). `StrikePurity` + `ExtraPitch` exported from
  score_verify.
- **User design decision:** purity judging is an *optional mod* for
  hardcore score tiers (osu analogy) -- game-judging-guide G6. Threshold
  must be calibrated on real-pedal baselines (MAESTRO) before convicting.

### Purity-mod calibration (G6) — the fact had to be fixed before the mod

- **PASS-13 — measured before shipping a threshold (the whole point).**
  Raw unexplained-energy on 1,574 sampled real strikes: p50 0.038 /
  p99 0.633 — the synthetic mash (0.061) sits INSIDE the clean
  distribution. A raw threshold would have convicted innocents; caught
  here, before any game rule existed.
- **PASS-14 — diagnosis and fix: the sounding set is not the key-hold
  set.** The keyed-1 band showed p50 0.305 (pedal-held notes miscounted
  as unexplained). With a pedal-aware candidate set (released notes stay
  candidates 1500 ms after note-off), the clean baseline collapses ~10x:
  p50 0.004 / p95 0.038 / p99 0.097.
- **PASS-15 — per-keyed-density bands measured** (batch 03f): 2-4 keyed
  p50 0.007 / p95 0.047 / p99 0.095 etc.; the mash signal (0.061) lands
  above p95 of its band. G6 conviction rule = per-band threshold +
  player-relative anomaly (T1 style) + two-signal corroboration.
- **NOTE-4 — fit-failure tail (unexplained = 1.0) must stay unjudged**
  (the soft-strike misses); exclusions recorded in the guide.

### Still open

- ISS-3 verification at folder scale (batch 03, running: the Chopin file's
  miss rate should drop now that nothing is truncated by the candidate cap).
- Purity-mod baseline calibration on real strikes (pedal/resonance floor)
  once batch 03 lands.

---

## Performance: why throughput fell (user question)

- **ANSWER — hardware acceleration is active** (the hot Gram loop is
  AVX2-vectorized; verified in code). The fall had two implementation
  causes and one design cost:
  1. **Cache regression (mine):** raising validator capacity 10 -> 64
     changed the Gram stride 120 -> 768; the Cholesky walked 3 KB jumps.
     Fixed with a packed stride (rows n_atoms_ apart).
  2. **No convergence exit (mine):** dense windows spent 4 refine rounds
     even after the fit had converged. Fixed: exit when the search moves
     < 0.5 c (the heavy cavalry leaves when the fight is won).
  3. **The heavy cavalry itself (the approved tradeoff):** 2 passes / 4
     rounds in dense windows is genuinely more work than batch 01's
     single pass.
- **Measured recovery:** 6.96x -> 7.14x real time on 5 jobs with
  *identical* outputs (percentiles and saturation counts bit-equal).
  The remaining gap vs batch 01's 15.2x is cost item 3 — effort where
  density demands it. Levers if ever needed: adaptive fit-step in sparse
  regions, per-window effort scaling.

## Batch 03 — full 2004 folder with all fixes (132 files)

- **Date:** 2026-10-07
- **Scope:** whole extracted folder, 23.2 h audio, **815,032 notes**, all
  fixes in (saturation flags, release-based end rule, smooth register
  bands, heavy cavalry)
- **Command:** `kan_dataset_bench --maestro data/maestro-v3.0.0 --jobs 12
  --out runs/batch03`
- **Results:** found **97.3%** (flat vs batch 02: 97.27 -> 97.29%); |cents|
  p50 4.9 / p95 36.3 / p99 61.1 c (max 119 = honest bass exploration in
  the wide band); duration err mean +208 ms, **p50 210 / p95 246 / p99 249
  ms**, max 3078 ms (was 9564); **9,828 saturated estimates flagged**
  (1.21%); per register: low p50 19.1 / p95 76.1 · mid 4.0 / 28.2 · high
  5.4 / 31.3 c.

### Good passes

- **PASS-11 — duration facts are tight at scale.** p99 = 249 ms across
  815k notes: the release bias is stable and the truncation tail is gone
  (40 rows > 600 ms err, 0.005%).
- **PASS-12 — saturation honesty at scale.** 1.21% of estimates now say
  "ran out of range" instead of a fabricated cap number; all excluded from
  pitch facts and from the judge's reach.

### Issue revisions (honesty rule)

- **ISS-3 REVISED — cap truncation was real but NOT the main miss cause.**
  The heavy cavalry recovered only 19 of the Chopin file's 318 misses
  (98.3 -> 98.4%). Every note is now measured (the regression test proves
  no truncation), but the residual misses concentrate exactly where the
  documented conditioning limit lives: miss rate **5.02% high (>72) vs
  1.71% mid vs 2.44% low**. Dense top-octave clusters degrade the joint
  fit and honestly resolve as not-verified. Options if this matters for
  gameplay (it likely does not: game judging is per-strike, <=10 sounding
  notes): interference-cancellation iterations, sparse solvers (OMP),
  velocity priors — all measurement-semantics changes through the §4
  checklist.
- **NOTE-3 — heavy cavalry costs throughput.** 7.28x -> 4.22x real time on
  12 jobs (5.5 h wall for the folder; full MAESTRO projected ~45 h wall).
  `--fit-step` triage mode and more year folders in parallel remain the
  scaling levers; GPU stays out (T3).

---

## Batch 02 — full 2004 folder (132 files) — running

- **Date:** 2026-10-07
- **Scope:** all extracted audio (year folder 2004), 12 jobs
- **Command:** `kan_dataset_bench --maestro data/maestro-v3.0.0 --jobs 12
  --out runs/batch02`
- **Status:** in progress; results and analysis to be appended here.
  Note: batch 02 runs the pre-fix binary — its outputs measure the old
  end rule and unsaturated pitch stats by design (the batch log shows the
  tool as it was when the batch ran; batch 03 carries the fixes).

---

---

## Batch 05 — full battery on unseen material (year 2008, 147 files)

- **Date:** 2026-10-08
- **Scope:** players/recording unseen by any tuning, 14.7 h audio,
  **572,660 notes**, 49,049 purity strikes; full battery (technique at
  30 ms trajectory + purity + all night fixes)
- **Pre-registered expectations vs measured:**

| validation | expected | measured (2008) | verdict |
|---|---|---|---|
| found-rate | ~97.5% | **97.6%** | PASS |
| \|cents\| p50 / p95 | ~4 / ~25-37 c | 3.8 / 29.1 c | PASS |
| per register | low weaker | low 13.4/62.1, mid 3.2/22.7, high 4.2/27.0 | PASS |
| duration bias | +210±10 ms | p50 234 / p99 250 ms | PASS (same family) |
| saturated | ~1.2% | 0.77% (4,306) | PASS |
| vibrato (negative control) | <= 0.2%, ~10 Hz residual | **0.10%**, 11.11 Hz | PASS |
| sustain / hold | ~0.27 / ~1.4 | 0.31 / 1.45 | PASS |
| purity (recalibrated) | mash 0.061 > band p95 | keyed 2-4 p95 **0.034** | PASS |
| throughput | ~3-4x | 1.39x (fit-step 30 + battery) | NOTE |

Three years of material (2004/2006/2008) now confirm the same measured
properties — the evidence class that backs the game's claims.

- **ISS-5 RESOLVED — `end_faded` certified by the envelope matrix** (the
  named follow-up, executed): 4 controlled envelope shapes (clean release,
  decay crossing, pedal ring, short note) all classified correctly. Two
  real bugs were found and fixed en route: (1) `lvl_before` reached back
  to the attack peak so slow decays looked like cliffs (fix: local window
  only), (2) the release cliff falls BETWEEN samples after the crossing
  (fix: the cliff is the continued fall after the crossing sample — 24 dB
  in fixture A vs 0.7 dB in the decay case).
- **ISS-6 (the big one) — I8's "release bias" is mostly the measurement
  window's own padding.** On real piano, 96% of notes are still sounding
  above threshold at the window edge (key-hold + 250 ms): `end_faded`
  honestly says "end never observed", and `measured_end` pins at the
  window cap. The famous stability of p99 = 249 ms across three years is
  stable **because 250 ms is a constant of the method**, not a property
  of piano releases. Reinterpretation: duration facts are currently
  "written + ~window padding" — stable, calibratable, coarse. True
  release detection (running past the window, damper-cliff tracking) is
  the named path to sub-100 ms duration facts. Everything published
  before this entry interpreted the bias as release decay; the numbers
  stand, their meaning is corrected here.
- ~~ISS-5 provisional~~ (superseded by the resolution above).

- **ISS-5 — `end_faded` is PROVISIONAL (do not rely on it yet).** Batch 05
  caught it over-firing at 71%; two criterion fixes (fixed 300 ms window
  mis-flagged short notes; proportional slope mixed plateau and cliff)
  brought it to 37%, but the remaining pattern is unexplained (short
  notes flag MORE: 43% <100 ms vs 11% >=1 s — pedal-vs-grid physics
  unresolved). Per our own standard, an under-validated flag is not a
  fact: duration stats remain stable either way (both groups show the
  same +234 ms bias), the game must NOT use `end_faded` until an
  envelope-type fixture matrix (release / decay-fade / pedal-ring / short
  note) validates the criterion. Named open item below.

## The soft-high pocket: experiments, one fix kept, one rejected

Root-cause work on PASS-24's pocket (user: "try it!"). The decisive
experiment measured the pocket's victims: missed soft-high notes had
**good amplitude (0.0067) and correct pitch (8 c)** but sim 0.36 — the
*metric* was failing, not the measurement: the similarity denominator
(window energy) is dominated by the noise floor in soft passages, and by
unmodeled ringing.

- **PASS-25 — noise-floor-referenced similarity (kept).** `similarity` is
  now the explained fraction of the energy ABOVE the estimated noise
  floor (5th-percentile frame RMS), with the honest guard: no signal
  above floor -> similarity 0 (absent-note semantics preserved; the full
  counterexample suite passes unchanged). vel02: high-soft pocket
  13.67% -> 12.59%, overall 97.32% -> 97.51% — modest recall gain, but
  it also sharpens the G6 purity separation (recalibrated: mash 0.061 vs
  its band's p95 0.037, was 0.047).
- **REJECTED — pedal-aware candidate sets inside measure_score.** The
  purity search's pedal fix (strike-sampled) tempted the same for the
  score core. It broke the dense fixtures (near-collinear extended sets
  fail the Cholesky) and exploded cost (5 files > 30 min). Reverted in
  full; kept only in `search_extra_pitches` where it is strike-sampled
  and validated. Recorded so nobody retries it without new evidence.
- **The velocity-prior hypothesis (the original plan) was disproved by
  the experiment above**: the pocket's notes are fitted with correct
  amplitude and pitch — an amplitude prior has nothing to fix. What is
  left is physics (low SNR in soft high register): see the recall table
  in `game-judging-guide.md` G2 (published as instrument facts so judges
  never convict unresolvable notes — path 3 of the plan).

## Miss-pocket decomposition (velocity ground truth, vel01: 38,817 notes)

- **PASS-24 — root-cause map of the ~3% miss rate** (MIDI velocity now in
  the ground truth; analysis asserts CSV field alignment — see NOTE-6):
  - by velocity: soft (<=p25=50) **4.73%** -> loud (>p75=78) 1.54%
  - by register x velocity: **high soft 13.67%** (the dominant pocket),
    high loud 3.70%, low soft 4.43%, low loud 1.17%, mid soft 1.47%,
    mid loud **0.16%**
  - by duration: mild gradient (2.1% <60 ms -> 3.4% >=300 ms)
- **Interpretation (physics, consistent with the known limits):** soft
  high-register notes carry little energy (fast decay, weak fundamentals
  that double as other series' harmonics — the conditioning limit's
  cousin), so their facts degrade first. Mid-register loud notes are
  nearly always measurable. This decomposes the earlier "dense high
  register" pocket into register x dynamics, not density alone.
- **Proposed fixes (checklist-gated, measurement-semantics changes):**
  (a) velocity-aware amplitude prior to regularize weak-note fits
  (ground-truth velocity says how loud the note should be); (b) longer
  analysis windows for low-SNR notes; (c) accept and publish the recall
  table as instrument facts (the game's judge must not convict a player
  for a soft-high note the measurement cannot resolve — G2 honesty
  extended by register AND dynamics).
- **NOTE-6 — a CSV column was silently missing (velocity) while its
  header claimed it** (an unreplaced patch pattern; caught because the
  analysis read all-1 velocities). Field-count alignment is now asserted
  in analysis scripts; lesson recorded alongside the patch-discipline one.

## Technique facts — bend emitted (schema v3) + vibrato resolvability gate

- **PASS-22 — bend facts now flow (schema v3).** Audit found BendAnalyzer
  dead code advertised as shipped; the stream now emits `bend_c` /
  `bend_err_c` on EXPRESSION (glide displacement + linear-fit error),
  disambiguated by exclusion: no bend when a vibrato claim owns the record
  (oscillation vs glide) or when a legato transition began on it (a step
  is a TRANSITION record). Presence gate |glide| >= 25 c. Validated by
  `note_stream_test` fixture 12 (plucked +100 c glide claimed; straight
  note claims nothing; no spurious transitions). JSONL schema v2 -> v3.
- **PASS-23 — vibrato resolvability gate.** A claimed rate must sit below
  0.8x the trajectory's own Nyquist (residual claims sat at ~11 Hz on the
  50 ms stream cadence = above its 10 Hz Nyquist: noise, not measurable
  oscillation). Piano negative control: 0.19% -> **0.11%** in the 30 ms
  bench sampling (where 10 Hz is still resolvable); in the product's
  50 ms stream the same claims are rejected by the gate. The remaining
  residual (rate ~10 Hz, cadence-locked) lies outside every musical
  vibrato band and `VibratoAnalyzer::check()` against pack specs rejects
  it — fact reports honestly, the judge filters (division of labor held).

## Technique facts (EXPRESSION) — real-data validation (night 2026-10-08)

User request: validate the execution-shape labeling (sustain, vibrato,
dynamics) and get the capability trustworthy. Summary:

- **PASS-18 — positive control.** `dataset_bench --selftest` injects a
  5 Hz / 30 c vibrato: detected at 4.94 Hz / 30.2 c; straight notes are
  NOT claimed (the counterexample suite's mash of fit noise used to be
  claimed — see PASS-19).
- **PASS-19 — root cause of false vibrato claims fixed in the library.**
  `VibratoAnalyzer::periodic` was ">=3 zero-crossing cycles && depth >= 5"
  — any fit-noise wobble crossed zeros and won the label (synthetic
  straight notes claimed at 4.6-8.7 Hz, 14-53 c). Added **coherence gate**
  (sinusoid-fit variance explained >= 0.5) + **rate-stability gate**
  (first/second-half rates within 30%). `VibratoMeasurement` now reports
  `coherence` and `rate_stable` (facts, not verdicts).
- **PASS-20 — negative control on real piano (tech01, 10 MAESTRO files,
  71,302 notes): vibrato claimed on 0.19%** — piano cannot sustain
  vibrato. The residual claims (rate p50 11.11 Hz) are cadence artifacts
  near the 50 ms reading Nyquist, not vibrato territory; documented as
  the residual signature (a future rate-band gate would clear them).
- **PASS-21 — envelope/dynamics distributions plausible on real piano:**
  sustain_ratio p50 0.27 (decaying instrument), hold_ratio p50 1.41
  (consistent with the +200 ms release bias at ~500 ms notes).
- **ISS-4 (root cause) — long-note duration outliers diagnosed and
  flagged.** All 40 batch-03 outliers (>600 ms error) are NEGATIVE on
  notes written >3 s (median 3.7 s): monotone decay crosses the end
  threshold while the key is still held — no release to observe. Fix:
  `end_faded` fact (drop < 6 dB in 300 ms around the crossing = end
  inferred from decay, not observed) — faded ends are excluded from
  duration facts, never reported as confident ends (I2 pattern).
- **AUDIT-1 — repository audit (see Open items).** Notable find the user
  suspected: EXPRESSION vibrato was **structurally 0.0 on the score-
  informed path** (the game's main path) — vibrato measured only for
  blind records. Fixed: vibrato now measures for all records with pitch
  readings (20/20 ctest).

## Batch 04 — out-of-sample validation (year 2006, 115 files)

- **Date:** 2026-10-08
- **Scope:** unseen material (players/recording not used by any tuning),
  25.3 h audio, **864,448 notes**, 36,126 sampled strikes for purity;
  perf-fixed binary, `--purity --purity-tref 80 --pedal-ms 1500`
- **Results:**
  - **Quality metrics hold out-of-sample:** found 96.7%; |cents| p50 4.6 /
    p95 37.5 / p99 64.9 c (2004: 4.9/36.3/61.1); duration mean **+208 ms**
    and p50 211 / p95 246 / p99 249 ms (2004: 210/246/249 — essentially
    identical); low register p50 19.1 / p95 79.2 c. I3 and I8 are stable
    properties of the measurement, not of the corpus.
  - **Purity baseline: the center holds, the tails taught us a lesson.**
    p50 0.006 (2004: 0.004) but p95 0.064 (was 0.038) and p99 0.476 (was
    0.097). The 1,574-strike calibration sample **underestimated the tails
    5x** — small samples lie about tails. Final baseline now on 36k
    strikes.
  - **Tail composition (the decisive analysis):** strikes where the
    written set already fails verification carry the tail (p50 0.021 /
    p90 1.000, n=1,623). On fully verified strikes (n=34,503): p50 0.006 /
    p90 0.030 / p95 0.052 / p99 0.177.

### Conclusions (G6 rule refined)

- **PASS-16 — purity as a standalone conviction is not viable** (raw
  threshold would flag ~5% of clean strikes at the mash signal level).
  Documented honestly instead of shipped.
- **PASS-17 — the refined rule is viable:** purity anomaly is judged ONLY
  on fully verified strikes (miss-strikes stay unjudged, I2), acts as a
  **trigger for corroboration** (extra-pitch candidates + level-inflation
  signature must agree), and as a **player-relative anomaly** (T1 style).
  Trigger level from the big baseline: ~p95 of verified strikes (0.05).
- **NOTE-5 — calibration samples must cover the tails:** threshold work
  needs >= tens of thousands of strikes, not hundreds.

---

## Open items (pending — tracked here so nothing is lost)

Repository audit 2026-10-08 (full feature inventory) key findings:
BendAnalyzer is dead code (docs advertised bend as shipped — corrected;
emitting bend needs schema v3 + bend-vs-vibrato-vs-transition logic);
`kFlagDominant` can never fire and `BlindContent::dominant_midi` is
computed then discarded (decide: wire at CONTENT emission or remove);
`kan_cli` has zero automated coverage; technique `check()`/`TechniqueResult`
and `SpectralSignatureValidator` have no consumer (pack machinery awaiting
the game judge); `RelativeTuningTracker` + per-string tuning matrix
orphaned; KissFFT/ChromaChordValidator consumed by one test only;
`TapBuffer`/`PitchShifter` await kanfx; `kan_analyze --events` runs blind
only (never feeds candidates); InstrumentProfile `judgment_offset_ms`/
`attack_ramp_ms`/`family`/`attack_type` unread by libkan (game-side pack
data — comments corrected). Doc drift fixed: JSONL v1->v2 heading,
strike_group/n_unresolved names, EnvelopeAnalyzer name, test count.

**ISS-5 follow-up (named):** envelope-type fixture matrix for `end_faded`
(4 synthetic cases: clean release, monotone decay crossing, pedal-ring
extension, short note < 100 ms) + per-type re-measure on MAESTRO before
the flag becomes a fact. Duration facts themselves are validated and
stable without it.

Master status document (readiness, sharpening plan, cross-instrument
transfer and the dataset strategy for instruments without MAESTRO-grade
labels): `docs/architecture/validation-status.md`.

1. ~~Batch 04~~ **done** — see the batch 04 entry; baselines now on 36k
   strikes across two years of unseen material.
2. **G6 threshold finalization.** Numbers are now measured (verified-strike
   p95 0.05 trigger, two-signal corroboration, player-relative anomaly);
   write them into the pack schema before the game implements the mod.
3. **Duration outliers.** 40 rows (0.005%) with |dur_err| > 600 ms in
   batch 03 — inspect for pattern (pedal? repeated notes? wrong note-off?)
   before touching the end rule again (invariants checklist §4).
4. **Remaining year folders.** 2004 done (132), 2006 running (115); 2008,
   2009, 2011, 2013-2018 remain. Disk-limited: extract per folder, run,
   and keep the summary of each in this log. Full MAESTRO projection
   (~45 h wall at 12 jobs with the heavy cavalry) or `--fit-step` triage.
5. **Purity fact for the live stream.** `StrikePurity` currently lives in
   the offline verification core (score_verify). The game's live path
   needs the same fact from the note stream (chart-supplied candidates +
   pedal semantics in the profile) — design item, not yet implemented.
6. **T1 player intonation profile.** Contract documented; the game-side
   accumulation/calibration service does not exist yet.
7. ~~Bend facts (schema v3)~~ **done** — PASS-22 (fields + disambiguation
   + fixture 12).
8. ~~Vibrato residual gate~~ **done** — PASS-23 (Nyquist resolvability);
   residual ~10 Hz claims are cadence-locked and rejected by spec checks.
9. **Audit leftovers.** Wire-or-remove kFlagDominant + dominant_midi;
   kan_cli selftest; decide chroma/FFT keep-or-drop; wire kan_analyze
   candidate path; calibration orphans (RelativeTuningTracker, per-string
   matrix) need a consumer or removal.
10. **Known measurement limits (by design, documented).** High-register
   conditioning above ~25 simultaneous notes (sim degrades, facts resolve
   as not-verified); deep-bass saturation flags (1.2%); long-note duration
   errors beyond the release bias are rare but real.
