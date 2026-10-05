# ADR-0009: Ranked-status workflow & QA roles

- **Status:** Proposed (draft, design phase)
- **Deciders:** server/ + community maintainers (draft by Agent 6)
- **Related:** [docs/architecture/backend-data-model.md](../architecture/backend-data-model.md)
  §4, [server/TODO.md](../../server/TODO.md), osu! Beatmap Nominator (BN)
  precedent

## Context

Only `ranked` charts feed `kp` and appear on official leaderboards
([backend-data-model.md](../architecture/backend-data-model.md) §4). Unranked
charts keep scores but award no kp. The ranked gate is therefore the single
most important quality + fairness control in the ecosystem: it decides what
"competitive canon" means. osu!'s Beatmap Nominator model (community
volunteers with veto-capable votes, criteria docs, and a feedback queue) is
the proven precedent for a donation-funded community game; kan! adds one
osu!-has-not automation layer: `kan!check`, the biomechanical playability
linter, which can pre-screen charts mechanically before any human reads them.

The schema already fixes the vocabulary: `songs.ranked_status` and
`charts.status` over `enum(unranked, pending, ranked, loved, graveyard)`.

## Decision (proposed)

### 1. State machine

```
                 submit for review
  unranked ──────────────────────────► pending
     ▲                                    │
     │  disqualify / reset (mapper or     │ 2× qualify votes, 0 vetoes,
     │  QA, with audit reason)            │ kan!check PASS (hard gate)
     │                                    ▼
     └────────────────────────────────  ranked
                                        │      ▲
                          rank reset ────┘      └── (ranked → loved is a
  any ── mapper abandon / stale ──► graveyard      community "nomap" petition:
  any ── community favorite ──────► loved          no kp, no board, playable)
```

- Ranking is decided **per chart** (`charts.status`); `songs.ranked_status` is
  the roll-up shown in the catalog (highest status among its charts). A song
  with one ranked violin chart and one pending guitar chart is perfectly
  normal.
- Every transition writes an `audit_events` row (actor, reason, payload) —
  append-only, so ranked history is public and appealable.

### 2. Pipeline stages

**Stage 0 — upload.** `kan!check` runs automatically on every chart upload
(size gate `< 50 KB`, format validation, biomechanical lint per instrument
family). Result is stored with the chart. A FAIL blocks submission to
`pending` outright — no human time is wasted on mechanically broken charts.
Warnings (e.g. borderline fret jumps) do not block but are shown to reviewers.

**Stage 1 — pending / feedback queue.** Mappers submit charts to the review
queue. Reviewers (role below) leave structured feedback (timing accuracy,
chart-to-music representation, difficulty honesty, metadata/license hygiene —
license evidence is checked here, matching `songs.license` +
`audio_license_evidence`). Mappers iterate; the chart returns to the queue
when the mapper marks feedback addressed.

**Stage 2 — qualification vote.** Two distinct reviewers vote *qualify*; any
reviewer may *veto* (one veto = back to pending with mandatory written
reason). A chart qualifies only if `kan!check` status is PASS at vote time
(hard gate re-checked server-side — votes on a FAILed chart are rejected by
the API, not by convention). A short qualification freeze (72 h) follows,
during which any reviewer can still veto before ranked takes effect — the
osu! "qualify → ranked" bubble, which catches rushed votes.

**Stage 3 — ranked.** Board goes live; kp accrues (ADR-0008). Ranked charts
are immutable in gameplay-affecting ways: timing/note changes require a new
`song_versions` row or a new chart, and boards are reset with public audit
reason. Metadata-only fixes are allowed.

### 3. Roles

| Role | Powers | osu! analog |
|---|---|---|
| **Mapper** | upload, submit to pending, respond to feedback | mapper |
| **Chart Reviewer (CR)** | feedback, qualify vote, veto; scoped **per instrument family** (a percussion CR does not vote on bowed-string charts unless dual-rostered) | Beatmap Nominator |
| **QA Maintainer** | everything a CR can, plus: rank reset, forced graveyard/loved moves, roster appointments/removals, criteria doc ownership | QAT + BN managers |
| **Moderator** | anti-cheat scope only (verdicts, bans, score invalidation) — explicitly *cannot* rank/unrank charts (separation of powers) | GMT |

- CRs are volunteers appointed by QA Maintainers; appointment/removal is an
  `audit_events` record. Roster size targets: ≥3 CRs per instrument family
  before that family's boards are considered "healthy"; families below that
  still rank but with QA Maintainer as mandatory second voter.
- Conflict of interest: a reviewer cannot vote on their own chart or on a
  chart of a song they authored (server-enforced, not honor system).

### 4. Ranked criteria

A living document (versioned in-repo, owned by QA Maintainers) defines what
*must* pass to rank: sync accuracy, playability per family (from kan!check
rules), difficulty labeling honesty, licensing evidence, metadata standards.
Criteria changes are announced with a grace period; charts qualified under old
criteria are not retroactively unranked unless a hard fairness/legality issue
arises.

### 5. Loved & graveyard

- **Loved**: community petition (threshold: configurable signature count from
  users with any ranked play history — anti-Sybil gate) moves a chart out of
  ranked without deleting it: playable, scores stored, **no kp, no official
  board**. Escape hatch for fun-but-unrankable charts.
- **Graveyard**: mapper-abandoned or stale pending charts auto-move after an
  inactivity period (configurable, osu! uses ~1 month without update);
  revivable by resubmission.

### 6. API & enforcement surface

The workflow is enforced server-side (never client-trusted):

- `POST /charts/{id}/submit-review` (mapper; requires kan!check PASS)
- `POST /charts/{id}/feedback`, `POST /charts/{id}/vote` {qualify|veto|reset}
  (reviewer roles; server re-checks kan!check status, COI, and roster family)
- status transitions emit `audit_events`; appeals are moderation-adjacent
  tickets against those records.

## Consequences

- Human review time is spent only on charts that already pass mechanical
  screening — the kan!check hard gate is what makes a volunteer QA model
  scalable to 100k songs.
- Per-family reviewer scoping keeps instrument-specific playability knowledge
  (bends vs bowing vs breath windows) in the loop; it also means slow roster
  growth in niche families — mitigated by the QA-maintainer second-vote rule.
- Ranked immutability + versioned charts guarantees board integrity: a ranked
  board never silently changes difficulty under players' feet.
- Everything above maps onto the existing schema (status enums, audit_events,
  song_versions); no data-model change required to implement it.
