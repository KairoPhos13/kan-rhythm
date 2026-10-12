# game/

The **kan!** game client, built on **osu!lazer** (`ppy/osu`, MIT) per
[ADR-0010](../docs/adr/0010-osu-lazer-base-client.md): a fork + kan ruleset
(game mode) + custom screens (calibration, instrument packs, **kan!forge**
editor). Integration architecture:
[docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).

**Status:** Milestone 2 (vertical slice) — see [TODO.md](TODO.md).

## Contents

- `ruleset/` — kan ruleset plugin (C#): highway, dual-axis judgment,
  instrument helpers, mods, P/Invoke bridge to libkan/kanfx
- `screens/` — kan-specific screens (calibration, instrument packs,
  kan!forge)
- `client/` — git submodule of the **kan** fork (github.com/KairoPhos13/kan,
  osu!lazer rebranded per ADR-0011: standalone **kan!** game, not a mod)
- `docs/` — fork divergence log (every fork-level change, why, and its
  upstream-PR status)

## Ground rules

- English only (root `CONTRIBUTING.md`)
- **Minimize fork divergence**: prefer ruleset-plugin code over fork edits;
  every fork delta documents its merge-conflict cost and upstream-PR status
  in `game/docs/fork-divergence.md`
- **License compliance is ship-blocking** (ADR-0010): keep upstream headers
  and license text, ship `THIRD_PARTY_NOTICES`, mark substantially changed
  files, contribute fixes back upstream, resolve BASS licensing before any
  donation-driven release
- Real-time safety: instrument capture and judgment stay in libkan/kanfx
  (C++); osu!framework audio is used only for music/SFX playback
- C#/.NET 8 + CMake toolchain; extractable into the `kan-osu` repo later
