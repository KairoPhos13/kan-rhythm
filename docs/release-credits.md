# Release credits and licence sign-off

A checklist a maintainer reads **before publishing a release zip**. It says
what ships in each zip, under which licence, and where players see the
credit. Gaps found by the audit are marked **GAP** with a proposed fix and
a suggested issue title. When in doubt about a component's licence the
doubt is recorded here, never guessed
([issue 26](issues/26-third-party-credits-audit.md), its Notes rule).

- **Audit run:** 2026-11-11, `agent/credits` lane.
- **Fix pass:** 2026-11-11, `agent/credits-fix` lane: GAP 1, 2, 3, 4, 5,
  7 and 8 fixed as recorded in section 4; GAP 6 stays open (owner's call).
- **Fix pass:** 2026-11-12, `agent/release-hardening` lane (November 2026
  audit section 7): GAP 9, 10 and 11 fixed as recorded in section 4
  (licence texts in every zip, pinned + hashed separation model, portable
  CPU baseline), plus the build/CI hardening of that audit (pinned
  workflow actions, warnings as errors in CI, version stamps, no symbols
  in the player zips).
- **Audited against:** superproject `agent/credits` worktree, fork pin
  `e000468941` (read-only), NuGet cache and `osu.Desktop/obj/project.assets.json`
  of a restore of that pin. The fix pass regenerated the notices against
  the pin in force (`da2f355dad`, fork F-29); its
  `project.assets.json` matches that pin's package versions.
- **Policy of record:** [CONTRIBUTING.md §6](../CONTRIBUTING.md),
  [ADR-0003](adr/0003-mit-license-no-gpl-policy.md) as amended by
  [ADR-0013](adr/0013-accept-inherited-lgpl-set.md),
  [SPECIFICATION.md §1](SPECIFICATION.md),
  [osu-integration.md](architecture/osu-integration.md) §4.
- **Player-facing summary:** [docs/releases/v0.1.0.md](releases/v0.1.0.md).

## 1. What ships in each zip

The zips are `kan-win-x64.zip`, `kan-osx-arm64.zip`, `kan-osx-x64.zip` and
`kan-linux-x64.zip`, assembled by
[`.github/workflows/build.yml`](../.github/workflows/build.yml) through
`game/packaging/windows/assemble.sh` (same steps for a local build). The
macOS zip holds `kan!.app` (`Contents/MacOS/` is the same payload,
`Contents/Resources/kan.icns` is generated from the brand logo,
`Info.plist` is stamped with the version). `apps/` and `tools/` are
developer tools and stay out (`KAN_BUILD_APPS=OFF`, `KAN_BUILD_TOOLS=OFF`).
The separation model is **not** shipped (see row 11).

| # | Ships in the zip | Licence | Credited to players where |
|---|---|---|---|
| 1 | .NET runtime and the `kan` launcher (self-contained `osu.Desktop` publish) | MIT (Microsoft .NET library licence) | Notices §3 (package list) |
| 2 | osu!lazer fork: `osu.Game`, osu!framework (`ppy.osu.Framework`) | MIT, Copyright ppy Pty Ltd and contributors | About screen line "Built on osu!lazer (MIT), ppy/osu contributors"; notices §1; root [README](../README.md) "Credits and licence"; release notes "Thanks" |
| 3 | NuGet closure of `osu.Desktop`: 140 packages (kan!'s own assemblies add only `ppy.ManagedBass`, already listed here) | per package, see notices §3 | Notices §3 (in-game about screen) |
| 4 | `ppy.osu.Game.Resources`: UI art, UI sounds, translations, bitmap fonts | **CC BY-NC 4.0**, © ppy Pty Ltd, used non-commercially; **fonts under their own licences** (named in notices §1 since 2026-11-11; the Torus/Venera question is open, GAP 6) | Notices §1 |
| 5 | BASS, BASSMIX, BASSFX, BASSWASAPI (un4seen Developments) | proprietary, **free for non-commercial use** only ([game/docs/bass-licensing.md](../game/docs/bass-licensing.md)) | Notices §2 (points at the analysis and un4seen terms) |
| 6 | FFmpeg 4.x (`libavcodec`/`libavformat`/`libavutil`/`libswscale`), via `ppy.osu.Framework.NativeLibs` | LGPL 2.1 or later (build-dependent, see GAP 3); accepted as the inherited set by [ADR-0013](adr/0013-accept-inherited-lgpl-set.md) | Notices §5 (corrected 2026-11-11, GAP 3) |
| 7 | SDL2 and SDL3, `libveldrid-spirv` (SPIRV-Cross), `libstbi`, `librealm-wrappers`, `e_sqlite3` | SDL2/SDL3: Zlib; SPIRV-Cross: MIT; stbi: public domain/MIT; Realm: Apache-2.0; SQLite: public domain | packages in notices §3; native binaries named in notices §5 (GAP 4) |
| 8 | kan! payload: `osu.Game.Rulesets.Kan.dll`, `Kan.Forge.dll`, `Kan.Core.dll` | MIT, kan-rhythm contributors | root README; release notes |
| 9 | `libkan_forge_native` (the native bridge): miniaudio (MIT-0), Basic Pitch port + ICASSP 2022 weights (Apache-2.0, Spotify AB), GGML (MIT), BSRoformer.cpp (MIT) | MIT-0 / Apache-2.0 / MIT / MIT, record of record [libkan/THIRD_PARTY_NOTICES.md](../libkan/THIRD_PARTY_NOTICES.md) | Notices §5 (bridge components named 2026-11-11, GAP 4) |
| 10 | `brand/`: kan!'s logo, icons, intro art, menu backgrounds | MIT, kan-rhythm contributors (provenance in [game/brand/README.md](../game/brand/README.md), fixed 2026-11-11) | kan!'s own art, no third-party credit owed |
| 11 | Separation model (BS-RoFormer SW 6-stem GGUF, ~180 MB): **not shipped**, downloaded to `models/` at first use from a **pinned, hash-verified** source (revision + SHA-256, GAP 10, since 2026-11-12) | **default f16 grade: MIT** (`studio409`, since 2026-11-11); q8 grade (`FractalSurfer`, licence unknown) is an explicit opt-in only | Notices §6 (model, source, licence) + release notes (GAP 7) |

Credit surfaces that exist today: the in-game **about screen**
(Settings > General > About kan!, fork entries F-5/F-6) showing the
embedded `THIRD_PARTY_NOTICES.md`, the repository's
[README](../README.md) "Credits and licence", the fork README, the
release notes "Thanks", and (since the 2026-11-11 fix pass) the
`THIRD_PARTY_NOTICES.md` and `LICENCE` files shipped in every zip
(GAP 8), with the full licence texts in the `licenses/` folder beside
them (since 2026-11-12, GAP 9).

## 2. Sign-off checklist

Work top to bottom. "Fix" items were fork-side or needed a decision, so
they lived in the GAP list and were not done in the audit itself; the
fix pass of 2026-11-11 closed them as recorded in section 4.

- [x] `THIRD_PARTY_NOTICES.md` regenerated for the current dependency set
      with the generator corrections of GAP 3, GAP 4 and GAP 5 folded in
      (2026-11-11). The fork is read-only in the fix lane, so the file
      waits at [game/tools/out/THIRD_PARTY_NOTICES.md](../game/tools/out/THIRD_PARTY_NOTICES.md)
      for the fork lane to commit it into the fork (GAP 1).
- [ ] About screen still embeds and shows the notices (F-5/F-6). Verified
      in code on 2026-11-11; a running-client look is still worth one
      minute.
- [ ] BASS: this release is free, takes no money and gives no perks, so
      the free-use clause covers it ([bass-licensing.md](../game/docs/bass-licensing.md)
      §4.1). The hard gate is unchanged: **no donation-driven or paid
      release** until the un4seen answer, a purchased licence, or the
      `kanplay` replacement (bass-licensing.md §5/§6, issue 25).
- [ ] osu!'s CC BY-NC 4.0 content (UI sounds, art, translations) is used
      non-commercially and credited in notices §1; osu!'s voice and theme
      music are never played (fork F-22); the osu! name and logo appear
      only as attribution and trademark reference.
- [x] LGPL-family components inherited from osu!lazer (FFmpeg.AutoGen,
      TagLibSharp, OpenTabletDriver ×4, plus the FFmpeg binaries of row 6):
      the owner decision is recorded as [ADR-0013](adr/0013-accept-inherited-lgpl-set.md)
      (2026-11-11, GAP 2): accepted while they stay separate assemblies
      and libraries loaded dynamically and their licence texts stay
      reachable, with a re-review before any paid release.
- [x] Separation model (GAP 7): the MIT f16 grade
      (`studio409/BSRoformer-SW-6Stem-GGUF`) is the default first-use
      download since 2026-11-11; the unlicensed q8 grade is an explicit
      opt-in (`KAN_ROFORMER_GRADE=q8`) only. Notices §6 names the model,
      its source and its licence state.
- [x] Fonts (GAP 5): the notices name Inter, Noto, Torus, Torus-Alternate
      and Venera with their licence state (2026-11-11). Still open
      (GAP 6): the Torus/Venera redistribution question needs an owner
      answer.
- [x] The zip carries the notices and the licence as readable files
      (GAP 8): `assemble.sh` copies both into the payload of every
      platform and `build.yml` puts them at the macOS zip root next to
      `kan!.app` (2026-11-11).
- [x] The zip carries the licence **texts** in `licenses/` (GAP 9): one
      list (`game/packaging/licenses.manifest`) drives the copies,
      `game/packaging/check-licenses.sh` fails the build when a listed
      file is missing from the payload or the repository, and CI runs it
      as its own step (2026-11-12). Which text covers which component:
      `licenses/README.md`.
- [x] The separation model downloads from a pinned, immutable revision and
      passes a SHA-256 check before the GGUF loader sees it (GAP 10,
      2026-11-12): a wrong hash deletes the file and says so calmly;
      `KAN_ROFORMER_GGUF` stays the trusted escape hatch for a local copy.
- [ ] `game/brand/` files have author + licence (done 2026-11-11,
      [game/brand/README.md](../game/brand/README.md)); hand-made art that
      replaces the provisional files brings its own provenance line.
- [ ] `assets/` ground rules hold ([assets/README.md](../assets/README.md)):
      no GPL/AGPL assets, provenance in a `CREDITS.md` per subfolder. The
      folder is still a placeholder, so nothing to record today.
- [ ] Release notes read well as the player-facing credit
      ([v0.1.0.md](releases/v0.1.0.md), "Thanks" points at the in-game
      notices and the zip files; extended 2026-11-11 with the separation
      model's licence line).

## 3. Audit record (issue 26)

### 3.1 NuGet closure vs `THIRD_PARTY_NOTICES` (issue item 1)

`game/tools/gen-third-party-notices.py` was run against the fork's
`project.assets.json` with the output redirected to a temp file (the fork
is read-only in this lane; nothing in it was overwritten). Result:
**140 packages, identical set** to the shipped notices; the upstream MIT
text (fork `LICENCE`, Copyright (c) 2025 ppy Pty Ltd) is intact; the BASS
pointer (§2) matches [game/docs/bass-licensing.md](../game/docs/bass-licensing.md).

The shipped notices are **stale on versions**: they were generated
2026-10-12, before fork F-18's security patches (MessagePack 3.1.8 to
3.1.12, SignalR and Microsoft.Extensions 10.0.12 to 10.0.13). 39 rows
differ. See GAP 1.

The generator's soft warnings (licence "(unknown)" or "see URL") were
resolved by reading the actual licence sources:

| Package | Notices say | Verified 2026-11-11 |
|---|---|---|
| `HidSharpCore` 1.3.0 | (unknown) | Apache-2.0 (upstream `LICENSE` of HIDSharpCore) |
| `ppy.managed-midi` 1.10.2 | see URL | MIT (license file ships inside the package) |
| `ppy.osuTK.NS20` 1.0.211 | (unknown) | MIT/X11-style (OpenTK library licence, plus permissive Mono/Tao/OpenEXR portions) |
| `ppy.SDL2-CS` 1.0.741-alpha | (unknown) | Zlib-style (the SDL2# wrapper's upstream licence text) |
| `ppy.Veldrid`, `.MetalBindings`, `.OpenGLBindings`, `ppy.Veldrid.SPIRV`, `ppy.Vk` | see URL | MIT (upstream `LICENSE` files) |
| `SQLite` 3.53.4 | custom | public domain (SQLite blessing) |
| `ppy.osu.Game.Resources` | see URL | CC BY-NC 4.0 + per-font licences (section 3.4) |

The generator's hard warnings (LGPL family, inherited from upstream
osu!lazer) stand: `FFmpeg.AutoGen` (LGPL), `TagLibSharp` (LGPL-2.1-only),
`OpenTabletDriver` and its three satellites (LGPL-3.0-or-later). See GAP 2.

### 3.2 BASS terms vs `game/docs/bass-licensing.md` (issue item 1)

Consistent. The notices' §2 states the free-for-non-commercial terms, the
© Un4seen Developments credit and the "resolve before any donation-driven
release" pointer, which is exactly the hard gate in bass-licensing.md §6
item 5 (ADR-0010 §5). The native binaries that carry the terms ship on every
platform (`libbass`, `libbassmix`, `libbass_fx`, plus `basswasapi` on
Windows); the ManagedBass C# wrappers are MIT and listed in notices §3.
For v0.1.0 (free, no money, no perks) the free-use clause applies; any
paid supporter perk, ad or funded entity reopens it.

### 3.3 `game/brand/` provenance (issue item 2)

Every file in `game/brand/` is provisional art generated from scratch by
`game/tools/gen-provisional-brand.py` (geometry, no font, no osu! art):
`Textures/Menu/logo.png`, `Textures/Icons/Logo.png`,
`Textures/Intro/Triangles/logo-background.png` and `logo-highlight.png`,
and `Textures/Menu/menu-background-1.jpg` … `-8.jpg`. The folder README
named the generator and the design but not author and licence, which is
the missing provenance line fixed on 2026-11-11 (small doc fix in the
superproject). `assets/` remains an empty placeholder, so its
`CREDITS.md` rule has nothing to cover yet.

### 3.4 osu! resources credited (issue item 2)

`ppy.osu.Game.Resources` (CC BY-NC 4.0, © ppy Pty Ltd) still supplies the
UI sounds, UI art and translations, and its bitmap fonts. Used
non-commercially, credited in notices §1, shown on the about screen. The
fonts upstream carry their own licence files:

| Font | Upstream licence file (ppy/osu-resources) |
|---|---|
| Inter | `OFL.txt`, SIL Open Font License |
| Noto (basic, CJK, Hangul, Thai subsets) | `LICENSE.txt`, SIL Open Font License |
| Torus, Torus-Alternate | `LICENCE`: "This font requires a commercial licence for distribution" (the designer's site, via MyFonts) |
| Venera | `LICENCE`: "This font requires a commercial licence for distribution" (via LostType) |

The notices say only "fonts under their own licences" (GAP 5) and the
Torus/Venera redistribution terms are not recorded anywhere in kan!'s docs
(GAP 6). osu!'s voice ("welcome to osu!") and theme music are never
played (fork F-22), so the highest-exposure CC BY-NC audio is out of the
picture.

### 3.5 Native and tooling dependencies vs ADR-0003 (issue item 3)

All within MIT / MIT-0 / BSD / Apache-2.0 (ADR-0003,
[libkan/TODO.md](../libkan/TODO.md) Rules):

| Component | Where it is used | Licence | In a notices file? |
|---|---|---|---|
| miniaudio 0.11.25 | native bridge, `tools/`, `apps/` | MIT-0 | added to [libkan/THIRD_PARTY_NOTICES.md](../libkan/THIRD_PARTY_NOTICES.md) 2026-11-11 |
| Basic Pitch ICASSP 2022 weights + C++ port | `libkan_basic_pitch`, compiled into the bridge | Apache-2.0 (Spotify AB), `libkan/licenses/` | `libkan/THIRD_PARTY_NOTICES.md` |
| GGML (pinned) | `libkan_roformer`, compiled into the bridge | MIT | `libkan/THIRD_PARTY_NOTICES.md` |
| BSRoformer.cpp (pinned) | `libkan_roformer`, compiled into the bridge | MIT (verified from the upstream `LICENSE`) | `libkan/THIRD_PARTY_NOTICES.md` |
| Cycfi Q v1.5.0 | optional `libkan_bacf` (apps/tests) | MIT | `libkan/THIRD_PARTY_NOTICES.md` |
| KissFFT 131.2.0 | optional `libkan_fft` (tests) | BSD-3-Clause | `libkan/THIRD_PARTY_NOTICES.md` |
| Signalsmith Stretch (pinned) | optional `libkan_stretch` | MIT | `libkan/THIRD_PARTY_NOTICES.md` |

`kanfx/` is still design-only (no code, no dependencies), and `tools/`
builds on libkan and miniaudio only. The release bridge is built
standalone (`game/forge/native`), so it contains miniaudio, Basic Pitch,
GGML and BSRoformer.cpp, but not the optional BACF/FFT/stretch backends.
What is missing is the player-visible half: none of these appear in the
client's `THIRD_PARTY_NOTICES` (GAP 4).

### 3.6 Separation model downloaded at first use (issue item 2)

`libkan/include/libkan/transcribe.hpp` downloads one GGUF model to
`models/` on first separation (`KAN_ROFORMER_GGUF` for a local file,
`KAN_MODEL_URL` for own hosting, `KAN_ROFORMER_GRADE` picks the grade):

| Grade | Source | Card licence (Hugging Face, checked 2026-11-11) |
|---|---|---|
| q8 (default) | `FractalSurfer/BS-RoFormer-SW-6stem` | **unknown** |
| f16 | `studio409/BSRoformer-SW-6Stem-GGUF` | MIT (mirror of `elicwhite/bs-roformer-sw-6stem-onnx`, MIT) |
| q4 | none public, falls back to q8 | unknown |

The model is credited nowhere players see (the release notes only say the
first separation downloads a model once). See GAP 7. This matches
[game/TODO.md](../game/TODO.md) "Separation model (owner decision)".

**Update (2026-11-12):** the table above is the record of the 2026-11-11
audit; since GAP 7 the default is the MIT f16 grade, and since GAP 10
both grades are pinned to immutable repository revisions and verified
against a pinned SHA-256 before use.

### 3.7 `server/app` npm production dependencies (issue item 2)

Audited the production closure of `server/app/package-lock.json`
(lockfile v3; dev-only entries excluded): **143 packages, 149 resolved
versions**. Licence field read from registry.npmjs.org for every
name@version, upstream `LICENSE` files for the two packages whose npm
metadata has none (`busboy`, `streamsearch`, both MIT by their upstream
`LICENSE` files).

| Licence | Count |
|---|---|
| MIT | 106 |
| Apache-2.0 | 27 |
| ISC | 9 |
| BSD-3-Clause / BSD-2-Clause / 0BSD | 2 / 1 / 2 |
| no npm metadata, MIT upstream | 2 (`busboy`, `streamsearch`) |

No GPL/AGPL/LGPL, nothing outside the ADR-0003 set. The direct
dependencies (`@aws-sdk/*` Apache-2.0, `@nestjs/*` MIT, `adm-zip` MIT,
`argon2` MIT, `dotenv` BSD-3-Clause, `multer` MIT, `pg` MIT,
`reflect-metadata` Apache-2.0, `rxjs` Apache-2.0) all comply.
`devDependencies` (442 lock entries: eslint, jest, typescript and friends)
are build-time only and are not deployed.

### 3.8 Compliance hygiene (issue item 4)

- Upstream copyright headers are intact on adapted fork files (spot-checked
  `osu.Game/OsuGameBase.cs`, `GeneralSection.cs`, `osu.Desktop/osu.Desktop.csproj`).
- Every kan! delta carries the `kan! rebrand (ADR-0011)` marker (38 files
  in the fork tree).
- "Substantially modified" marking: [fork-divergence.md](../game/docs/fork-divergence.md)
  records that no file qualifies (deltas are small and mechanical); the
  spot-check agrees.
- The notices are embedded (`osu.Game.csproj`, F-6) and rendered by
  `KanAboutSettings` (F-5) with the ppy attribution line.
- The fork README (F-25) credits osu!lazer and the MIT licence; the root
  README has a "Credits and licence" section.

## 4. GAP list

Each GAP is ready to be filed as its own issue; the title in bold is a
suggestion. GAPs that touch the fork need the fork lane (this audit did
not modify the fork). A **Status** line at the end of each entry records
what the fix pass of 2026-11-11 did (section 5 stays the audit's "could
not verify" list).

**GAP 1: regenerate `THIRD_PARTY_NOTICES.md` for the current build.**
The shipped file (2026-10-12) lists MessagePack 3.1.8 and the 10.0.12
Microsoft packages; the build has 3.1.12 and 10.0.13 since fork F-18.
*Proposed fix:* `dotnet restore game/client/osu.Desktop/osu.Desktop.csproj`,
run `game/tools/gen-third-party-notices.py`, review the diff (it should be
the date plus those version rows plus the GAP 3/4/5 additions), commit in
the fork. *Blocks publishing?* Yes for accuracy: the notices are the
licence record players see.
**Status (2026-11-11): fixed.** Regenerated against the fork pin in
force (`da2f355dad`; its `project.assets.json` matches the pin's
package versions, so no restore was needed). The diff is the date, the
F-18 version rows and the GAP 3/4/5/7 additions, as predicted. This
lane cannot edit the fork, so the file waits at
[game/tools/out/THIRD_PARTY_NOTICES.md](../game/tools/out/THIRD_PARTY_NOTICES.md)
for the fork lane to copy and commit it in the fork.

**GAP 2: the LGPL-family packages vs ADR-0003.** ADR-0003 says LGPL
"none are currently used", but the osu!lazer closure carries
`FFmpeg.AutoGen`, `TagLibSharp` (LGPL-2.1-only) and `OpenTabletDriver`
×4 (LGPL-3.0-or-later), plus the FFmpeg binaries of GAP 3. *Proposed
fix:* an owner decision recorded as a short ADR amendment: accept the
inherited set under LGPL terms (separate assemblies, dynamic loading,
licence texts reachable), or schedule replacements. *Blocks publishing?*
Not for a free release while the LGPL obligations are met, but the
documentation says something untrue today.
**Status (2026-11-11): fixed.** Owner decision recorded as
[ADR-0013](adr/0013-accept-inherited-lgpl-set.md): the inherited set is
accepted under LGPL terms (separate assemblies and native libraries,
dynamic loading, licence texts reachable, re-review before any paid
release). ADR-0003 itself is not edited: accepted ADRs are immutable
([docs/README.md](README.md) ground rules), so the amendment lives in
the new ADR, linked from here and from [SPECIFICATION.md](SPECIFICATION.md)
§0.

**GAP 3: notices §5 is wrong about FFmpeg.** It says FFmpeg is optional
and user-provided and that kan! does not redistribute it; in fact
`ppy.osu.Framework.NativeLibs` ships FFmpeg 4.x (`libavcodec` 58,
`libavformat` 58, `libavutil` 56, `libswscale` 5) in **every** zip. The
exact build configuration (LGPL-only or GPL-enabled) was not determined
here; treat as LGPL 2.1 or later until ppy's build flags are confirmed.
*Proposed fix:* correct the generator's section 5 (FFmpeg redistributed
via osu!framework's native libraries, LGPL 2.1+ with its licence
obligations), and while there fix the DiscordRPC description (it is a
Discord RPC wrapper, MIT, and presence is off since F-20). Regenerate
with GAP 1. *Blocks publishing?* The disclosure should be right before
strangers download.
**Status (2026-11-11): fixed.** Notices §5 now says FFmpeg 4.x
(`libavcodec` 58, `libavformat` 58, `libavutil` 56, `libswscale` 5) is
redistributed in every zip via `ppy.osu.Framework.NativeLibs` under
LGPL 2.1 or later (build-dependent), and the DiscordRPC line names
`DiscordRichPresence` (MIT) and that Discord presence is off since F-20.
The build-configuration doubt stands (section 5).

**GAP 4: the notices omit the shipped native components.** The kan!
bridge (`libkan_forge_native`) compiles in miniaudio (MIT-0), the Basic
Pitch port and its ICASSP 2022 weights (Apache-2.0), GGML (MIT) and
BSRoformer.cpp (MIT), and the zips also carry SDL2 (not just SDL3),
`libveldrid-spirv`, `libstbi`, `librealm-wrappers` and `e_sqlite3`.
None of these are named in the player-visible notices.
*Proposed fix:* extend `gen-third-party-notices.py` section 5 from
[libkan/THIRD_PARTY_NOTICES.md](../libkan/THIRD_PARTY_NOTICES.md) (which
is the record of record and was completed with a miniaudio entry on
2026-11-11), then regenerate. *Blocks publishing?* It is a completeness
gap in the credits, not a licence violation.
**Status (2026-11-11): fixed.** Notices §5 names SDL2 and SDL3,
`libveldrid-spirv` (SPIRV-Cross), `libstbi`, `librealm-wrappers`,
`e_sqlite3`, BASSWASAPI and the bridge's compiled-in miniaudio, Basic
Pitch port and ICASSP 2022 weights, GGML and BSRoformer.cpp, with
[libkan/THIRD_PARTY_NOTICES.md](../libkan/THIRD_PARTY_NOTICES.md)
pointed at as the record of record.

**GAP 5: fonts are not named in the notices.** "fonts under their own
licences" hides Inter (SIL OFL), Noto (SIL OFL), Torus and Torus-Alternate
(commercial distribution licence), Venera (commercial distribution
licence). *Proposed fix:* name them in the notices' resources line (same
generator edit as GAP 3/4). *Blocks publishing?* No, but CC BY-NC and font
credits are exactly what players are entitled to see.
**Status (2026-11-11): fixed.** The notices' resources line names Inter
and Noto (SIL Open Font License) and Torus, Torus-Alternate and Venera
(commercial distribution licence required per their upstream licence
files).

**GAP 6 (decision): Torus and Venera say distribution needs a commercial
licence.** Their upstream licence files in ppy/osu-resources say so, and
the bitmap versions ship inside every kan! zip through
`ppy.osu.Game.Resources`. Whether ppy's redistribution arrangement covers
downstream redistribution is not documented publicly.
*Proposed fix (owner chooses):* keep shipping with the attribution of
notices §1 and this doubt recorded; replace those fonts through
`game/brand/` with an OFL font (kan! replaces resources file by file
anyway); or buy the distribution licences. Suggested issue title:
**"Fonts: Torus and Venera redistribution terms"**. *Blocks publishing?*
Owner's risk call; recorded, not guessed.
**Status (2026-11-11): still open.** Not this lane's decision; the
doubt stays recorded here and the notices now state what the fonts'
upstream licence files say (GAP 5).

**GAP 7 (decision): the default separation model has no licence and no
credit.** The default q8 grade (`FractalSurfer/BS-RoFormer-SW-6stem`) has
licence "unknown" on its Hugging Face card; the f16 grade
(`studio409/BSRoformer-SW-6Stem-GGUF`) declares MIT. The client downloads
the default at first use and nothing tells the player what it is.
*Proposed fix (owner chooses):* make the MIT f16 grade the default, or
host a vetted copy and set `KAN_MODEL_URL`, or keep the default and
disclose it; in every case add a notices line naming the model, its
source and its licence state (this is also the pending
[game/TODO.md](../game/TODO.md) "Separation model (owner decision)"
item). Suggested issue title: **"Separation model: licence and credit for
the first-use download"**. *Blocks publishing?* Recommend resolving before
the first public download reaches players.
**Status (2026-11-11): fixed.** Owner decision: the MIT f16 grade
(`studio409/BSRoformer-SW-6Stem-GGUF`) is the default first-use
download; the q8 grade (`FractalSurfer`, no licence declared) is an
explicit opt-in (`KAN_ROFORMER_GRADE=q8`) and q4 falls back to the
default. The default lives in `roformer_default_grade()`
([libkan/include/libkan/transcribe.hpp](../libkan/include/libkan/transcribe.hpp),
with an offline test in `libkan/tests/transcribe_cmd_test.cpp`). Notices
§6 names the model, source and licence state; the
[game/TODO.md](../game/TODO.md) "Separation model" item is closed.

**GAP 8: no licence or credits file in the zip.** The notices exist only
inside `osu.Game.dll` and are readable by running the game. MIT asks that
the permission notice travel with copies, and players sometimes read
licences offline. *Proposed fix:* copy the fork's `THIRD_PARTY_NOTICES.md`
into the payload in `game/packaging/windows/assemble.sh` (and mention it
in the release notes), or ship the fork `LICENCE` next to it. *Blocks
publishing?* Not strictly, cheap to do.
**Status (2026-11-11): fixed.** `game/packaging/windows/assemble.sh`
copies `THIRD_PARTY_NOTICES.md` and `LICENCE` into every payload and
fails the build when either is missing; `.github/workflows/build.yml`
puts both at the macOS zip root next to `kan!.app`;
[releases/v0.1.0.md](releases/v0.1.0.md) tells players about them.

**GAP 9: the zip carries no licence TEXTS (November 2026 audit C3).**
GAP 8 shipped the notices and the fork's MIT `LICENCE`, but the
redistribution conditions of the other licences are not met by a
reference alone: Apache-2.0 (Basic Pitch, compiled into the bridge)
requires the licence text and notice to travel with a copy, and the LGPL
set accepted by [ADR-0013](adr/0013-accept-inherited-lgpl-set.md) requires
its licence texts to be reachable. kan!'s own MIT `LICENSE` was not in the
zip either. *Proposed fix:* a `licenses/` folder in every archive driven
by one list, checked in CI. *Blocks publishing?* Yes for the Apache-2.0
and LGPL obligations; a missing MIT text is at least sloppy.
**Status (2026-11-12): fixed.** Every archive carries `licenses/` beside
`LICENCE` and `THIRD_PARTY_NOTICES.md`:
`LICENSE-kan.txt` (kan!'s MIT),
`basic-pitch-LICENSE` + `basic-pitch-NOTICE` (Apache-2.0),
`LGPL-2.1.txt` (TagLibSharp, the FFmpeg 4.x native libraries),
`LGPL-3.0.txt` + `GPL-3.0.txt` (FFmpeg.AutoGen, OpenTabletDriver; LGPL-3.0
incorporates GPL-3.0's terms, so both texts travel) and a `README.md`
mapping each text to its components. The ONE list is
`game/packaging/licenses.manifest`; `game/packaging/check-licenses.sh`
fails the build when a listed file is missing from the payload or the
repository, and when a file in `libkan/licenses/` is not listed. CI runs
the check as its own step after `assemble.sh` (which runs for every
platform), and the macOS zip also carries `licenses/` at its root.

**GAP 10: the separation model downloaded from a floating branch with no
hash check (November 2026 audit C4).** The default URL pointed at
`/resolve/main/...` on a third-party Hugging Face repository: the branch
moves, and the ~200 MB file went straight from `curl` into the GGUF
loader. A changed, swapped or truncated model would either break
separation or be parsed blindly. *Proposed fix:* pin the URL to an
immutable revision and verify a SHA-256 before use. *Blocks publishing?*
Recommend fixing before the first public download reaches players.
**Status (2026-11-12): fixed.**
[libkan/include/libkan/transcribe.hpp](../libkan/include/libkan/transcribe.hpp)
pins both grades to repository revisions (f16: `fcb2eb0f`, q8: `2b98254b`)
and verifies the file against a pinned SHA-256 before the loader sees it
(`roformer_model_sha256_for` / `roformer_model_ok`; offline tests
`libkan_transcribe_cmd_test` and `libkan_model_pin_test`). A wrong hash
deletes the file and says so calmly, and a failed download leaves no
partial file. `KAN_MODEL_URL` may mirror the same pinned file (the digest
still applies; `KAN_MODEL_SHA256` pins an operator's own mirror) and
`KAN_ROFORMER_GGUF` stays the trusted escape hatch for any local copy.

**GAP 11: the shipped native library was tuned to the build host
(November 2026 audit C2).** ggml's CMake optimizes for the build
machine's CPU unless `GGML_NATIVE` is OFF (and with it OFF it still
enables AVX2-era x86 kernels by default), so `kan_forge_native` could die
with "illegal instruction" on older player CPUs; the root CMake also set
`-march=native` globally. *Proposed fix:* an explicit, documented
baseline in the shipped library's own build. *Blocks publishing?* Yes:
every player on older hardware is a crash report.
**Status (2026-11-12): fixed.**
[libkan/cmake/kan_build_policy.cmake](../libkan/cmake/kan_build_policy.cmake)
defines the baseline (x86-64-v2 on x86_64 with ggml's x86 kernels capped
at SSE4.2; arm64 on Apple silicon; MSVC at the x64 floor, which is even
more portable) and `game/forge/native/CMakeLists.txt` enforces it for
every build path of the bridge. `-march=native` is opt-in at the root now
(`KAN_MARCH_NATIVE=ON`, local developer builds only) and no packaging
script or workflow uses it; CI and `cross-build-native.sh` pass
`-DGGML_NATIVE=OFF` explicitly on top.

## 5. What this audit could not verify

- The fork-side commit of the regenerated notices: lanes of the
  superproject cannot write the fork, so the hand-off file
  (`game/tools/out/THIRD_PARTY_NOTICES.md`) waits for the fork lane.

- The about screen in a running client (verified in code only: F-5 loads
  the embedded resource, F-6 embeds it).
- A real release zip: the payload inventory comes from `build.yml`,
  `assemble.sh`, the fork project files and the NuGet cache on this
  machine, not from unzipping an artifact.
- The FFmpeg build configuration behind `ppy.osu.Framework.NativeLibs`
  (LGPL vs GPL build): treated as LGPL 2.1 or later, doubt recorded in
  GAP 3.
- The full commercial terms of the Torus and Venera typefaces: only their
  upstream licence stubs were read (GAP 6).
- npm licence *texts* for the server: the registry `license` fields were
  read (plus upstream licence files for `busboy` and `streamsearch`), not
  each licence document.
- Whether Sentry telemetry is active in a shipped client (a privacy
  question, not a licence one; out of scope here).
