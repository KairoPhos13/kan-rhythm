# ADR-0008: `kp` (kan! points) formula (weighted top-N, difficulty multiplier, dual-axis mix)

- **Status:** Accepted (2026-10-05, confirmed by project owner). Constants
  (600, exponents, 0.65/0.35 mix ratio, w, N) are starting points tunable
  against real play data; changes post-launch require an ADR amendment +
  full kp recompute (see §4-5).
- **Deciders:** server/ + ranked-criteria maintainers (draft by Agent 6)
- **Related:** [docs/architecture/backend-data-model.md](../architecture/backend-data-model.md)
  §4, [docs/SPECIFICATION.md](../SPECIFICATION.md) (kp = osu!-pp-style
  weighted top-N with chart-difficulty multiplier)

## Context

`kp` is the osu!-pp analog: a per-user ranking points total computed from
ranked scores. kan! differs from osu! in one fundamental way: every score row
carries **two quality axes** ([backend-data-model.md](../architecture/backend-data-model.md)
§3, `scores` table):

- **Timing axis**: `count_perfect/great/ok/miss`, `timing_avg_ms`,
  `timing_dev_ms` (osu!-style hit judgments).
- **Intonation axis**: `accuracy_cents` (mean absolute pitch deviation in
  cents; the "sound good" axis unique to real instruments).

A keyboard MIDI player cannot bend pitch; a flute player lives or dies by
intonation. The formula must reward both axes without letting one mask the
other, and must keep boards segregated by `(chart_id, input_method, mod_mask)`
per the ROADMAP M4 fairness gate.

All inputs below are columns that already exist in the schema sketch; no new
data is required to compute kp v1.

## Decision (proposed)

### 1. Per-score value `kp_i`

```
kp_i = D(chart) · 600 · A^4 · C · M
```

**Accuracy blend `A` (dual-axis mix).**

```
T = (300·c_perfect + 100·c_great + 50·c_ok) / (300 · n_total)      # timing accuracy ∈ [0,1]
P = clamp(1 − accuracy_cents / 50, 0, 1)                           # intonation purity ∈ [0,1]
A = T^0.65 · P^0.35                                                # weighted geometric mean
```

- The **geometric** mean (not arithmetic) is deliberate: a run with perfect
  timing but 50-cent-average intonation (or vice versa) collapses toward 0.
  Neither axis can carry the other; this encodes "rhythm game *and* real
  music" in the ranking itself.
- **Mix ratio 0.65 timing / 0.35 cents** is the proposed default. Rationale:
  timing is the primary skill of a rhythm game and is measured on every note;
  intonation is instrument-dependent (percussion/`other` family and MIDI input
  have limited pitch agency). The ratio is a **ranked-criteria decision**: it
  is versioned (`kp_formula_version`, see §4), tunable, and must be re-ratified
  by ADR amendment before any change post-launch.
- **Input-method exemption:** for `input_method = midi` on fixed-pitch
  instruments, `P` is set from pitch-bend fidelity if available, else the
  score's `P` term is replaced by the board-average `P` (neutral) so MIDI
  players are neither rewarded nor punished for an axis their instrument
  cannot express. Boards remain segregated by `input_method`, so this only
  affects cross-board `kp` totals.

**Difficulty multiplier `D(chart)`.** Computed once at rank-time by
`kan!check` from chart features (not per play). The v1 schema sketch carries
no column for it, so until implementation adds a catalog-side store (follow-up
migration), `D` is derived deterministically from chart features on each
recompute and the inputs/version are recorded in `audit_events`:

```
D = 0.5 + 0.25·density + 0.15·tempo + 0.10·technique        # normalized features ∈ [0,1]
```

- `density` = notes/sec relative to the instrument-family cap from the
  biomechanical linter; `tempo` = BPM normalized over [60, 240];
  `technique` = fraction of the family's technique vocabulary used
  (bends, harmonics, double-stops, breath marks, cross-hand…).
- v1 keeps `D` deliberately coarse and inspectable; a strain-based star
  rating (osu!-style per-pattern difficulty) is the v2 path once real chart
  data exists.

**Combo factor `C` and miss penalty `M`.**

```
C = (max_combo / chart_max_combo)^0.5        # capped at 1
M = 0.97^(count_miss)                        # multiplicative miss penalty
```

`^4` on `A` mirrors osu!-pp's steep accuracy curve: top ranks must be clean
on both axes.

### 2. Per-user total `kp` (weighted top-N)

```
kp_total = Σ_{i=1..N} kp_(i) · w^(i−1),   w = 0.95,   N = 100
```

- Scores sorted descending; only `validity = 'valid'` scores on `ranked`
  charts count. One score per chart counts (the user's best) to prevent
  farm-by-repetition.
- The 0.95 decay means ~50 top scores carry >92% of the total, same
  shape as osu!-pp, proven to reward breadth + peak skill.

### 3. Per-instrument-family ranks

`kp_stats.instrument_ranks` (jsonb) stores
`{family: {kp, rank}}` computed by the same weighted top-N restricted to
charts whose pack `family` matches. Global and country ranks come from
`kp_total`. Recompute is async and debounced per user (§5 of the data model).

### 4. Formula versioning

Every computed value is tagged with `kp_formula_version` (integer, carried in
the recompute job and recorded in `audit_events` on change). A version bump
triggers a full async recompute of `kp_stats` and `leaderboard_entries`;
boards are never mixed-version. v1 = the formula above.

### 5. Rollout & tuning gate

Before ranked launch: run v1 over the closed-beta score corpus, publish the
kp distribution (top-1000 histogram), and check for degenerate farm patterns
(one chart dominating top-N). Constants (600, exponents, 0.65/0.35, w, N) are
config, not code; changes post-launch require an ADR amendment + full
recompute.

## Consequences

- Dual-axis identity is enforced mathematically: intonation matters for rank,
  which no other rhythm game offers; this is the "beyond osu!" surface.
- Geometric mixing will feel harsh to players with weak intonation early on;
  the 0.35 exponent on `P` and the MIDI exemption soften this. Expect
  community debate; the versioning + ranked-criteria process exists to absorb
  it without destabilizing boards.
- `D(chart)` from coarse features will misprice some charts at first; the
  one-score-per-chart + top-N decay limits the damage of any single mispriced
  farm chart, and rank-time recomputation of `D` (on chart update) is cheap.
- All computation is batchable in SQL/workers: no request-path latency
  impact, consistent with the scalability envelope.
