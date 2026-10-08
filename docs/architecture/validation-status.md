# Validation status & readiness — where we are, what is missing

**Date:** 2026-10-08. Evidence: `tools/dataset_bench/TEST_LOG.md` (batches
01–04), `libkan/tests/score_error_test.cpp`, user takes (Hallelujah,
Frieren). Invariants: `measurement-invariants.md`. Game-side contract:
`game-judging-guide.md`.

---

## 1. What is validated (measured, at scale)

| dimension | state | evidence |
|---|---|---|
| Presence (played / not) | 96.7–98.8% vs ms ground truth | 1.68 M notes, MAESTRO 2004+2006 |
| Pitch (amplitude-weighted center) | p50 ~4.5 c / p95 ~37 c (low register p95 ~78 c) | same; bands per register, saturation flagged (1.2%) |
| Duration | bias +208 ms (release), p99 249 ms, stable across years | same; release-based end rule |
| Mistake detection | wrong pitch / missing / extra / late / over-held / mash all leave measurable signatures; mash needs the refined G6 rule | counterexample suite (11 cases, in ctest) |
| Purity fact (mod) | calibrated on 36 k real strikes; viable as trigger + corroboration + player-relative | batches 03f/04 |
| Determinism & RT safety | live ≡ offline; callback pushes POD only; ≤2 analysis threads | fixtures I9/I12 |
| Performance | 3.6–7.1× real time offline (12 jobs); heavy-cavalry adaptive | batch timings |
| Extensibility | instruments are pack data (`InstrumentProfile`), zero instrument code | I10 |
| Execution-shape facts (EXPRESSION) | vibrato: 0.19% false claims on real piano (negative control) + injected vibrato detected 4.94/30.2 vs 5 Hz/30 c (positive); envelope/dynamics plausible; faded-out ends flagged, not guessed | tech01 + selftest controls (TEST_LOG PASS-18..21, ISS-4) |

**What this means:** the *measurement science* for piano is at the level of
a serious judged game. Every claim has a number, a corpus, and a documented
failure mode.

## 2. Readiness for an osu-like game on real instruments

The backend is ready to **prototype the game loop against the live stream**
today. A shippable game needs, beyond libkan:

1. **Game-side judge implementation** — consume facts, apply G1–G6 gates.
   Contract complete; code does not exist yet (game repo).
2. **Live-path purity/content facts** — `StrikePurity` lives in the offline
   verification core; the live stream needs chart-candidate-driven purity
   with pedal semantics (open item 5 in TEST_LOG).
3. **Per-player calibration services** — T1 intonation profile and the G6
   player-relative baseline (documented; service not built).
4. **Device input-latency calibration** — planned tool (`tools/TODO.md`);
   without it, timing judgment on consumer hardware is unfair.
5. **Pack schema for measured thresholds** — G6 numbers (0.05 trigger,
   bands), release bias, search bands must ship as pack data.
6. **End-to-end latency proof under game load** — the ~500 ms final-fact
   budget is untested next to a rendering/audio loop.
7. **A second instrument family validated** — see §4 (piano-only truth is
   the biggest scientific gap).

Not blockers for prototyping: 2, 3, 5 can be stubbed; 1 is the game itself.

## 3. Where to aim the remaining MAESTRO samples (sharpening plan)

Prioritized by *which numbers would change a design decision*:

1. **Purity-tail contexts (biggest lever for the mod).** On verified
   strikes the tail spans 0.05–0.18 (p95–p99). Root-cause it: resonance?
   bleed? notes in audio but not in MIDI? If the tail is mostly
   identifiable context, the G6 trigger can move below p95 and detection
   sharpens without false positives.
2. **Miss-rate pockets — decomposed (vel01, 38k notes).** The pocket is
   **soft + high register (13.67% miss)**; mid-loud is 0.16%. Register x
   dynamics, not density alone. Candidate fixes: velocity-aware amplitude
   prior (semantics change, checklist), longer windows for low-SNR notes,
   or publishing the recall table as instrument facts so the game judge
   never convicts a player for an unresolvable soft-high note.
3. **Duration outliers.** 40 rows > 600 ms error — pattern before any end-
   rule change (checklist §4).
4. **Generalization across recording eras.** Years 2013–2018 (different
   pianos/mics/players) with the same flags — one folder is enough to
   confirm the 2004/2006 stability.
5. **The user's own takes** — different instrument, room, and mic than
   MAESTRO; the best cheap check against corpus overfitting.

Method note: run analysis *targets* (specific files by their metrics in
`notes.csv`/`purity.csv`), not more blind volume. Volume already gave the
statistics; the remaining gains are in root causes.

## 4. Transfer to other instruments (and the dataset problem)

### Transfer matrix

| layer | carries over? | notes |
|---|---|---|
| Fact semantics (unresolved = never wrong; one estimate/row; saturated flags) | **100%** | instrument-independent honesty rules |
| Record model (per struck note, lifetimes, re-strike vs extension by energy) | **100%** | validated logic, pitch-agnostic |
| Judging contract structure (gates G1–G6, no raw-audio re-derivation, mods as opt-in) | **100%** | thresholds change, structure doesn't |
| Methodology (counterexample suite, invariants, batch logs, measure-before-legislate) | **100%** | this is the real product |
| Search bands / end_drop_db / blanking / judgment offsets / polyphony caps | **as data** | already `InstrumentProfile` (I10); values need per-family measurement |
| Harmonic-series pitch model | **families** | works for strings and air columns (piano, guitar, winds, bowed). **Not** for inharmonic idiophones (marimba, bells, glockenspiel: partial ratios are not integers) — those need a partial-ratio model, a real change |
| Onset detector (×3.2 jump rule) | **must re-validate per family** | tuned on hammered attacks; wind/bow ramps differ (profile offsets exist; recall untested) |
| Release/end rule | **mechanism yes, values no** | plateau-relative release works for decay and swell; `end_drop_db` needs per-family measurement |
| Purity/mod baseline | **mechanism yes, baseline no** | pedal-aware candidate idea generalizes (any resonance), numbers are piano-specific |

### The dataset problem (the honest constraint)

MAESTRO-grade ground truth (acoustic audio + ms-perfect labels) exists for
piano because the **instrument can label itself** (Disklavier). For other
instruments the options carry intrinsic error — so the validation strategy
must be a **pyramid**, and the claims must state what bounds them:

1. **Synthetic fixtures with known truth** (free per instrument): catch
   regressions; prove logic, not real-world accuracy. Already per-instrument
   via profiles.
2. **Small human-annotated real sets** (the accuracy layer): we train on
   *nothing*, so validation sets can be small and targeted — the 28-attack
   hand annotation pattern. Expert listening + waveform inspection gives
   ~ms-level onsets for dozens of notes; enough to claim "±X cents on this
   instrument" with an honest n.
3. **Self-labeling instruments where they exist**: MIDI-capable wind
   controllers (EWI/WX), MIDI guitars (Roland GR), digital/bowed-MIDI
   violins (Zeta) reproduce the MAESTRO pattern imperfectly (controller
   dynamics ≠ acoustic flute dynamics) — usable as *stress* corpora, with
   the coupling error documented.
4. **Weakly labeled sets (human-crowd or AI-transcription)**: **never**
   sources of accuracy claims — the label error is the floor of any number
   derived from them. Use only for robustness/stress and coverage, with
   claims explicitly bounded ("against weak labels"). This is the same
   honesty rule as I2: a number is only as good as its reference.
5. **Semi-assisted verification with sheet music** (our `--verify-score`
   pattern): the chart *is* the hypothesis; recording quality matters more
   than labels. Real score + one recording of a flute piece gives per-note
   measurements at Hallelujah-grade immediately.

**Practical consequence:** for each new instrument, the minimum honest
package is (1) fixture suite via profile, (2) ~30–100 hand-annotated real
attacks across registers/dynamics, (3) a documented expectation table like
this one. Large corpora are a bonus, not a requirement — and if the only
available labels are AI-generated, the published numbers must say so.

## 5. Future design notes (parked deliberately)

- **Heavy cavalry for winds → ensemble levels** (user idea, 2026-10-08;
  explicitly a LATER feature). The adaptive escalation already proven on
  piano (capacity up to 64 candidates, dense-window passes/refinement)
  extends naturally to ensemble settings: several wind voices sounding
  together drive the candidate count up, and the fit brings the effort
  the density demands. What it needs beyond today's code: **per-voice
  instrument profiles in one joint fit** (mixed timbres — ranges,
  intonation bands, release semantics per voice), chart-supplied
  hypotheses per voice (the game knows the parts), and multi-timbre
  validation corpora (see §4 pyramid — self-labeling controllers become
  attractive here). The mono/poly classification (`max_simultaneous_notes
  = 1` per profile) must become per-voice, not per-session.

---

*Cross-references:* `game-judging-guide.md` (G1–G6), `measurement-invariants.md`
(I1–I12, T1–T3), `tools/dataset_bench/TEST_LOG.md` (batches, open items),
`note-description-stream.md` (design of record).
