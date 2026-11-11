# ADR-0013: Accept the LGPL set inherited from osu!lazer (amends ADR-0003)

- **Status:** Accepted (2026-11-11, project owner decision)
- **Deciders:** project owner (KairoPhos)
- **Amends:** ADR-0003 (MIT license and no-GPL dependency policy)
- **Audit of record:** [docs/release-credits.md](../release-credits.md)
  (issue 26, 2026-11-11)

## Context

ADR-0003 allows LGPL dependencies only after explicit discussion and a
new ADR, dynamic linking only, and records "none are currently used".
Since ADR-0010 the client is the osu!lazer fork, and its dependency
closure inherits LGPL components from upstream:

| Component | Licence | Shipped as |
|---|---|---|
| `FFmpeg.AutoGen` 4.3.0.1 | LGPL | managed assembly (P/Invoke layer) |
| `TagLibSharp` 2.3.0 | LGPL-2.1-only | managed assembly |
| `OpenTabletDriver` 0.6.7 (+ `.Configurations`, `.Native`, `.Plugin`) | LGPL-3.0-or-later | managed assemblies |
| FFmpeg 4.x (`libavcodec` 58, `libavformat` 58, `libavutil` 56, `libswscale` 5) | LGPL 2.1 or later (build-dependent) | native libraries via `ppy.osu.Framework.NativeLibs`, in every zip |

Replacing them would mean forking osu!lazer's audio decoding, tag
reading and tablet input paths; keeping them is what upstream does and
what the base client decision (ADR-0010) accepted in practice. The
audit of 2026-11-11 confirmed that distribution is compatible with the
LGPL family while the components stay separate assemblies and native
libraries, loaded dynamically, with their licence texts reachable.

## Decision

The inherited LGPL set above is **accepted** under the LGPL terms,
with these conditions:

1. Every component stays a separate assembly or native library that is
   loaded dynamically; nothing LGPL is compiled or linked statically
   into MIT code.
2. Each component stays named in the client's `THIRD_PARTY_NOTICES.md`
   with its licence and source, and the licence texts stay reachable
   (they ship inside the NuGet packages; the FFmpeg binaries carry the
   LGPL terms per ppy's builds).
3. kan! does not widen their use beyond what upstream osu!lazer does.
   Permissive replacements are welcome, never required.
4. The set is re-reviewed before any donation-driven or paid release
   (the same gate as BASS, [game/docs/bass-licensing.md](../../game/docs/bass-licensing.md)
   §6) and whenever upstream adds an LGPL component: the notices
   generator's hard warning is the tripwire
   (`game/tools/gen-third-party-notices.py`).
5. **No GPL/AGPL dependencies**, unchanged from ADR-0003.

## Consequences

- ADR-0003's "none are currently used" is outdated from 2026-11-11.
  ADRs are immutable once accepted ([docs/README.md](../README.md)
  ground rules), so ADR-0003 is not edited and this amendment is the
  record of the change; readers of ADR-0003 find it through
  [docs/release-credits.md](../release-credits.md) and
  [SPECIFICATION.md](../SPECIFICATION.md).
- The FFmpeg licence state stays "LGPL 2.1 or later, build-dependent"
  until ppy's FFmpeg build flags are confirmed (doubt recorded in
  [docs/release-credits.md](../release-credits.md) GAP 3).
- The client's notices (section 5) now disclose the FFmpeg
  redistribution and the inherited LGPL set to players, so the
  obligation to inform travels with the zip.
- A future LGPL addition is not blocked, but needs its own amendment
  here with the same conditions.
