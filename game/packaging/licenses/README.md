# licenses/: licence texts in the release archive

Every kan! release zip carries this folder next to `LICENCE` and
`THIRD_PARTY_NOTICES.md` (audit C3, November 2026). `LICENCE` at the
archive root is the osu!lazer MIT licence of the game client this build is
forked from; `THIRD_PARTY_NOTICES.md` names every third-party component.
The full texts of the licences that require them to travel with a copy are
collected here.

| File | Licence | Covers |
|---|---|---|
| `LICENSE-kan.txt` | MIT | kan! itself: the kan assemblies (`osu.Game.Rulesets.Kan.dll`, `Kan.Forge.dll`, `Kan.Core.dll`), the native bridge `kan_forge_native`, `libkan`, kan!'s art and the tools. Copyright (c) kan-rhythm contributors. |
| `basic-pitch-LICENSE`, `basic-pitch-NOTICE` | Apache-2.0 | The Basic Pitch transcription model and its C++ port compiled into the native bridge (Copyright Spotify AB). Source: `libkan/licenses/`. |
| `LGPL-2.1.txt` | LGPL-2.1 | TagLibSharp (LGPL-2.1-only) and the FFmpeg 4.x native libraries `libavcodec`/`libavformat`/`libavutil`/`libswscale` (LGPL 2.1 or later, build-dependent) that ship in every zip through `ppy.osu.Framework.NativeLibs`. |
| `LGPL-3.0.txt`, `GPL-3.0.txt` | LGPL-3.0 (which incorporates the terms of GPL-3.0, so both texts travel) | FFmpeg.AutoGen (its NuGet package ships the LGPL-3.0 text) and OpenTabletDriver 0.6.7 with its `.Configurations`, `.Native` and `.Plugin` satellites (LGPL-3.0-or-later). |

The LGPL set is inherited from upstream osu!lazer and accepted under its
terms by [ADR-0013](../../../docs/adr/0013-accept-inherited-lgpl-set.md):
each component stays a separate assembly or native library, loaded
dynamically. The components are listed with their exact versions in
`THIRD_PARTY_NOTICES.md` (sections 3 and 5).

The source-to-archive mapping lives in ONE list:
[`game/packaging/licenses.manifest`](../licenses.manifest).
`game/packaging/windows/assemble.sh` copies every entry and
`game/packaging/check-licenses.sh` fails the build if any listed file is
missing from the payload, so a release cannot ship with an incomplete
`licenses/` folder.

The LGPL and GPL texts are verbatim copies of the Free Software
Foundation's texts (fetched from gnu.org on 2026-11-12). The Basic Pitch
texts are the copies the `libkan` repository carries.
