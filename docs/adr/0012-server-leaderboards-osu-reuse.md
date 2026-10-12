# ADR-0012: Server & leaderboards — osu! ecosystem reuse policy (with license boundaries)

- **Status:** Accepted (2026-10-12, project owner decision)
- **Deciders:** KairoPhos
- **Extends:** ADR-0010 (client base), ADR-0011 (rebrand), ADR-0003 (MIT, no-GPL policy)
- **License verification:** GitHub API, 2026-10-12 (see matrix)

## Context

The game client now builds on osu!lazer (ADR-0010). The owner asked to
extend the same strategy — take osu!'s excellent features, adapt them,
rebrand to kan! — to the **server and leaderboards**.

License verification of the osu! ecosystem (GitHub API, 2026-10-12):

| Repository | License | Adoptable? |
|---|---|---|
| `ppy/osu` (osu!lazer client) | MIT | ✅ Yes — ADR-0010 |
| `ppy/osu-tools` (performance/pp calculator) | MIT | ✅ Yes |
| `ppy/osu-server-spectator` (multiplayer/spectator, SignalR) | MIT | ✅ Yes |
| `ppy/osu-web` (website + API v2 backend) | **AGPL-3.0** | ❌ **No** |

AGPL is incompatible with our MIT/no-GPL policy (ADR-0003, CONTRIBUTING §6):
we cannot fork, adapt, or derive code from `osu-web`. This is a hard
boundary, not a preference.

## Decision

### 1. Backend stays ours (the modular monolith of backend-data-model.md)

Do **not** adopt osu-web as the server base. Our PostgreSQL modular monolith
+ OpenAPI contract (already designed) stays the architecture of record. It
also fits our model better: dual-axis scores, instrument-family boards, and
`.kan`/`.kchart` catalog are not osu! concepts, and osu-web's schema is
deeply beatmap/ruleset-bound anyway.

### 2. `kp` algorithm: adopt the osu! pp architecture (MIT code)

`ppy/osu-tools` / the difficulty+performance pipeline in `ppy/osu.Game`
(MIT) is the reference implementation of "performance points". Adopt its
**architecture**: difficulty attributes per chart → performance calculation
→ weighted top-N aggregation (the formula shape behind osu!'s pp). Then
adapt: add the intonation axis (dual-axis scores), per-instrument-family
and per-input-method weighting, and our chart-feature difficulty multiplier.
Adapted code keeps upstream headers + copyright (MIT obligations) and is
credited; substantially divergent parts are marked.

### 3. Client-side ranking UX: adapt from osu!lazer screens (MIT)

Score overlays on beatmap pages, personal-best markers, ranking panels,
profile/rank history graphs: all exist in the client (MIT, already our
base) — adapt and rebrand per ADR-0011. osu-web's *website* UX (beatmap
listing, user profiles, forum) may be emulated as clean-room design (see §5).

### 4. Multiplayer/spectator: adopt `osu-server-spectator` (MIT) later

When Milestone 4+ needs spectating/multiplayer, adopt this component
(MIT, C#, SignalR) instead of writing a protocol from scratch. Noted in
server/TODO.md as a deferred adoption; verify its API contract against our
OpenAPI at that time.

### 5. osu-web clean-room rule (AGPL contamination boundary)

No code from `osu-web` enters this repository — ever. Website/profile UX
may be emulated from **public behavior and screenshots** (ideas and UX
patterns are not copyrightable; code and creative expression are).
Contributors who have read osu-web source in depth should not implement
our web frontend for the corresponding areas, and any resemblance must be
independent reimplementation. This rule is a licensing-mandated extension
of "respetamos y amamos el open source": we honor AGPL projects by
staying clean-room, not by quietly violating their license.

## Consequences

- server/ gains a "kp engine" work item referencing osu-tools, and a
  deferred spectator adoption item; `server/design/threat-model.md` and
  migrations are unaffected.
- The website (future `kan-web` repo) is designed from our OpenAPI with
  UX emulation only; contributors get the contamination rule in writing.
- Our leaderboard semantics (`chart_id, input_method, mod_mask`, dual-axis
  accuracy_cents) remain the differentiator; osu! provides the pattern
  grammar, not the data model.
- If ppy ever relicenses osu-web to a permissive license, revisiting this
  ADR becomes worthwhile.
