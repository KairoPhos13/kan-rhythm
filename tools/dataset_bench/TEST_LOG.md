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
