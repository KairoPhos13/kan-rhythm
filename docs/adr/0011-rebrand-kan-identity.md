# ADR-0011: Full rebrand — osu! becomes KAN! (kanade, "you kan!"), a standalone game, not a mod

- **Status:** Accepted (2026-10-12, project owner decision)
- **Deciders:** KairoPhos
- **Extends:** ADR-0010 (osu!lazer base client)

## Context

ADR-0010 adopted osu!lazer as the client base. The product owner refined the
vision: keep what makes osu! great — menus, scoring presentation, song
select, mod system, settings, replay UI, the overall UX flow — while
redesigning the gameplay-critical visuals (judgment indicators, buttons,
in-game visualization/highway) to fit real-instrument play. And crucially:
the product must present as **KAN!** (from *kanade* 奏でる, "to play an
instrument"; in English, "you kan!") — a standalone game, **not an osu!
mod or fork-branded derivative**.

This is also legally the correct posture: **MIT covers copyright, not
trademarks**. The osu! name and logo are brand assets we must not use in
our product identity, regardless of the code license.

## Decision

### 1. Product identity

- Product name: **kan!** (wordmark lowercase with exclamation, osu!-adjacent
  rhythm-game spirit). Tagline: *"Grab your instrument. You kan!"*
- The client window title, about screen, installer, website, and store
  pages say **kan!** only. Upstream osu! appears exactly once: in the
  attribution/credits ("Built on osu!lazer (MIT) — ppy/osu contributors"),
  which is both a license obligation and a community-respect statement.
- Fork repository: **`KairoPhos/kan`** (github.com/KairoPhos/kan — created
  2026-10-12, moved to the KairoPhos organization 2026-10-27, the
  owner's public identity for the project); submodule path `game/client/`.

### 2. What we KEEP (osu! UX patterns, behavior and flow)

- Menu structure and navigation flow (main menu → song select → results)
- Scoring/combo/accuracy presentation *patterns* (numbers, counters, HUD
  layout grammar) — fed by our dual-axis judgment
- Song select UX (grouping, sorting, search, carousel patterns)
- Mod overlay and mod system infrastructure (osu! `Mod` classes)
- Settings screens, key binding UI, results screen, replay viewer
- Skin infrastructure and the osu! skin compatibility path (where concepts
  map cleanly)
- Multiplayer/spectator infrastructure (later milestones)

### 3. What we REDESIGN (kan! visual identity)

- Judgment indicators: our dual-axis feedback (attack timing + intonation
  lane) instead of osu!'s hit-burst vocabulary
- Buttons / HUD controls: instrument-centric visuals (fingering hints,
  breathing windows, intonation meters)
- In-game visualization: the note highway (single-lane per instrument
  voice) — approach circles are NOT our grammar; instrument-pack-driven
  visuals per platform-architecture.md §5
- Color palette, typography, logo, default skin, hit/miss SFX, menu music
- Combo/energy visuals tuned to sustain + intonation (string-glow,
  breath-fog, pitch-lane) rather than osu!'s cursor-trail aesthetic

### 4. Code-level rebranding policy (rebase-ability first)

- **Adapted upstream code keeps its `osu.Game.*` / `osu.Framework.*`
  namespaces.** Renaming them would create catastrophic merge conflicts
  with upstream for zero user-visible benefit. Brand is user-facing;
  namespaces are plumbing.
- **All new kan! code uses `Kan.*` namespaces** (`Kan.Ruleset.*`,
  `Kan.Audio.*`, `Kan.Forge.*`).
- User-facing strings in adapted upstream code are rebranded (osu! → kan!)
  and tracked in the fork-divergence log as mechanical, low-conflict deltas.
- Windows titles, assembly names, and package IDs for *our* artifacts say
  `kan`; upstream assembly references keep their names.

### 5. Trademark & attribution hygiene (ship-blocking)

- No osu! logo, wordmark, or distinctive skin elements in kan! branding,
  marketing, or the default skin.
- Credits screen and `THIRD_PARTY_NOTICES` name ppy/osu and contributors.
- Default-skin and UI assets are original or license-clear (assets/CREDITS
  per folder).
- If upstream ever objects to any residual presentation, we comply promptly.

## Consequences

- A rebrand workstream joins Milestone 2: string sweep, asset replacement,
  identity design (logo, palette, fonts, default skin), about/credits screen.
- The empty-ruleset milestone task now includes the window-title/branding
  baseline so the "not a mod" identity is visible from the first build.
- Keeping upstream namespaces means contributors familiar with osu!lazer
  onboard instantly; our divergence stays reviewable.
