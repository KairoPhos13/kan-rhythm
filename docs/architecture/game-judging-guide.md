# Game judging guide — measured learnings the game must implement

**Purpose.** libkan describes; the game judges. Every learning below is
*measured* (evidence: [`measurement-invariants.md`](measurement-invariants.md),
[`../../tools/dataset_bench/TEST_LOG.md`](../../tools/dataset_bench/TEST_LOG.md)).
This guide translates each one into a **game-side implementation
requirement**, so a future gameplay feature cannot silently violate the
balance of factors that makes judgment fair. Violating a requirement here
does not produce a "slightly worse" judge — it produces an unfair one.

Reference measurements (MAESTRO batch 03a, 37,241 notes vs millisecond
ground truth; Hallelujah user take, 74 notes):

| fact | measured |
|---|---|
| notes found (presence) | 98.8% at sim ≥ 0.60 |
| \|cents\| (excl. saturated) | p50 4.0 c, p95 31.2 c, p99 53.7 c |
| \|cents\| by register | low (<45) p50 16.4 / p95 67.2 · mid p50 3.7 / p95 25.3 · high p50 3.7 / p95 29.1 |
| duration err (release bias) | mean +209 ms · p50 210 / p95 246 / p99 249 ms |
| saturated pitch estimates | 390 / 36,781 found (1.1%) — flagged, excluded |
| extra strikes (played, not written) | 29 / 37,241 (0.08%) |

---

## 1. Judgment gates (measured → rule)

### G1 — "Played or not" is presence/sim, never amplitude

Amplitude can be leased from neighbor leakage (a never-played note reported
0.12 amp while its sim collapsed to 0.06 — I7). The game decides
played/not-played from **sim/presence**; amplitude is a loudness fact only.

### G2 — Pitch: gate on |cents|, with the player's own calibration

- **sim alone never acquits**: a wrong note kept sim 0.91 while bending
  −56 c (I6). Correctness lives in the cents, presence in the sim.
- **Player intonation profile (T1)**: accumulate the per-note `cents` facts
  over judged-good play (median per pitch region), treat that as the
  player's bias, and judge the **bias-corrected residual** with a tight
  band (±30–40 c). This is stricter *and* fairer than a fixed ±60 c band.
- **Never judge a `saturated` estimate** (ISS-1): a number at the search
  cap means "ran out of range", not "detuned". The game's only fair action
  for that note's pitch dimension is: do not judge it (or count it as
  unjudged).
- **Weight by register**: low-register pitch facts are measurably weaker
  (p95 67 c vs 25 c mid). Either loosen the band below midi ~45 or treat
  low-register pitch as advisory; do not convict a bass note with a
  treble-grade band.

### G3 — Timing: strike facts and the peak signature

HIT timestamps are the strike times (records open at the first measurement
slot but keep the strike time — I4/§7 of the design doc). For score-anchored
verification the `t_peak` offset is the visible late/early signature. A note
more than ~100 ms off its written onset shows it reliably.

### G4 — Duration: judge against the release bias, not raw measurement

Measured durations carry a systematic **+~200 ms piano release bias** (I8;
MAESTRO: p50 210 / p99 249 ms — tight and stable). Rules:
- error is `measured − written − bias`; only errors **beyond the bias and
  its spread** are player-visible;
- the bias is **instrument-pack data** (plucked/wind releases differ —
  `end_drop_db` and the release semantics come from the pack profile).

### G6 — Purity mod (optional — user design decision)

**Design (by analogy with osu! mods):** casual play does not penalize
mashing (pressing random keys that include the right ones — measured: the
note gate alone passes the cheat); purity judgment is an **optional mod**
enabled for the hardcore score tiers. The mod makes a score mean "played
the notes *and nothing else*".

Measured facts (score_error_test case 9: correct F-major chord vs the same
chord mashed with 3 random keys):

| fact | clean | mash |
|---|---|---|
| strike unexplained energy (1 − sim0) | 0.1% | **6.1%** |
| written-set level (absorption) | 0.35 | 2.10 (inflated ~6×) |
| extra-pitch candidates (recall / noise) | 0 | 13 (all 3 true keys among them, plus neighbors) |

- **Primary fact: per-strike unexplained energy** (`StrikePurity`). Robust,
  no false-accusation risk on clean strikes. The mod's threshold is
  **pack data calibrated on real takes** (pedal and resonance leave a
  non-zero baseline — measure it from MAESTRO before any conviction).
- **Secondary: extra-pitch candidates** (`ExtraPitch` list). Total recall
  but noisy neighbors — informational material for a replay UI
  ("these keys sounded at this strike"), never the judge.
- **Absorption signature**: when extras are mashed, the written notes'
  fitted levels inflate (2-6×) and their cents bend (−25 c measured). A
  level-vs-expectation check (player calibration, T1) is a third,
  corroborating signal.
- **The penalty is game policy**; libkan emits the facts only. Suggested:
  the mod gates a precision sub-score and/or a score multiplier on
  hardcore tiers, exactly like osu!'s difficulty mods.

### G5 — Extra notes: set comparison at strikes

A played-but-not-written note is visible as an *unmatched strike* (T2), and
**chart-supplied hypotheses** per strike turn the whole question into a set
comparison: the game knows exactly which notes should sound at each strike,
so extra/missing voices are decided by evidence, never by blind fitting.
Recommended: the game feeds its chart candidates to the describer
(`--candidates` path) instead of relying on the blind generator.

---

## 2. Compute scheduling: heavy cavalry on demand

The measurement escalates effort with density (all alive notes fitted
jointly; extra passes/refinement above 10 alive — validated at the real
Chopin density peak of 27 alive). The game has an advantage the offline
bench lacks: **the chart knows the density in advance**.

- Schedule heavy analysis windows *ahead* of dense passages (big chords,
  fast runs) and keep light windows elsewhere; total budget stays
  ≤ 2 analysis threads / ≤ 4 cores (I11), latency for final facts ~500 ms.
- Dense-passage facts may arrive closer to the latency limit; the HIT
  timestamp never moves (records open early, judge on strike time).
- Beyond ~25 simultaneous sounding notes the joint fit's conditioning
  degrades in the top octaves (sim drops, facts resolve as "not verified").
  Past that point the stream emits *unresolved*, and the game must treat
  unjudged dimensions as unjudged (see §3).

---

## 3. Semantics the game must not break

- **`-` / unresolved = not measured.** Never "wrong", never "missing".
  The judge's only fair action is to not judge that dimension.
- **Never re-derive verdicts from raw audio in the game.** Consume the
  facts; a judge that peeks at the waveform behind the facts cannot be
  audited.
- **Instruments are pack data** (I10): a flute or melodica changes
  `InstrumentProfile`, never game code paths.
- **Live and offline behave identically** (I9): replays and live plays must
  judge the same, so practice mode and ranked mode agree.
- **Changes to any threshold above are analyzed decisions** — see the
  checklist in `measurement-invariants.md` §4. If a gameplay feature needs
  a different band or bias, it comes back as a measured change here first.

---

*Cross-references:* judging contract (`measurement-invariants.md` §3),
accepted tradeoffs T1/T2/T3, stream semantics
(`note-description-stream.md`), evidence runs (`tools/dataset_bench/`).
