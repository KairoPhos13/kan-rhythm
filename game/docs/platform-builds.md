# game/: release builds per platform + platform audit

How kan! is built and shipped for Windows, macOS and Linux, and what in the
codebase is Linux-only or untested. Written 2026-11-09 in the `agent/ci`
lane. **Nothing here has run on real Windows/macOS hardware yet: CI
(`.github/workflows/build.yml`) is the first build machine for them**; move
items out of the "untested" lists as they get verified.

Companion files: `.github/workflows/build.yml` (the build machine),
`game/packaging/macos/Info.plist` + `kan.entitlements` (bundle metadata),
`game/docs/environment.md` (Linux build environment),
`game/docs/fork-divergence.md` (what is changed inside the osu!lazer fork).

## 1. What ships where

| Platform | RID | CI runner | Native bridge | ggml backend | Microphone capture |
|---|---|---|---|---|---|
| Windows 10/11 x64 | `win-x64` | `windows-latest` | MSVC (VS 2022) | **Vulkan** | BASS → WASAPI |
| macOS Apple Silicon | `osx-arm64` | `macos-latest` | AppleClang | **Metal** | BASS → CoreAudio |
| macOS Intel | `osx-x64` | `macos-15-intel` | AppleClang | **Metal** | BASS → CoreAudio |
| Linux x64 | `linux-x64` | `ubuntu-latest` | GCC | **Vulkan** | BASS → ALSA/PulseAudio |

> The original plan said `macos-13` for the Intel build. GitHub **retired**
> the `macos-13` runner image; the Intel labels are `macos-15-intel` and
> `macos-26-intel` now (docs.github.com → "GitHub-hosted runners
> reference"). The workflow uses `macos-15-intel`.

ggml backend selection lives in `libkan/CMakeLists.txt` behind one CMake
variable, `KAN_GGML_BACKEND` = `AUTO | VULKAN | METAL | CPU` (default `AUTO`:
Metal on Apple, Vulkan when the SDK + `glslc` are found, CPU otherwise).
`KAN_DISABLE_VULKAN=1` is kept as the old spelling of `CPU`. At run time the
separator always keeps its CPU fallback (`BSR_FORCE_CPU=1` forces it), and
the Metal shader is compiled into the dylib (`GGML_METAL_EMBED_LIBRARY`), so
the app bundle carries no loose `.metal` file. CI passes the backend
explicitly per platform (table above).

Everything else is managed .NET 10 (`net10.0`), self-contained per RID, so
players install nothing.

## 2. What the workflow does (`.github/workflows/build.yml`)

Triggers: push and PR to `main`, and tags `v*`. Four matrix jobs (one per
platform above), then a `release` job on tags. Per job:

1. Checkout + `game/client` submodule (`.gitmodules` uses an SSH URL, CI has
   no key: the workflow rewrites `git@github.com:` to HTTPS first).
2. .NET 10 SDK, NuGet cache, CMake FetchContent cache (`_deps`).
3. Vulkan SDK on Windows and Linux only
   (`jakoch/install-vulkan-sdk-action`, cached). macOS needs no SDK for
   Metal. If the SDK were missing, the CMake configure would fall back to
   the CPU backend and print which backend it picked.
4. Native bridge: `cmake -S game/forge/native ... -DKAN_GGML_BACKEND=...
   -DGGML_NATIVE=OFF -DKAN_WERROR=ON -DKAN_VERSION=...`, build
   (`--parallel 6`), then its offline self-test (`ctest`). The bridge's
   own CMake enforces the portable CPU baseline (x86-64-v2 on x86_64,
   arm64 on Apple silicon; `libkan/cmake/kan_build_policy.cmake`,
   November 2026 audit C2) and never tunes to the runner's CPU;
   `KAN_WERROR=ON` makes first-party warnings fatal (audit C6) and
   `KAN_VERSION` stamps `kan_forge_version()` (audit C8).
5. libkan's DSP tests: root CMake with `-DKAN_BUILD_APPS=OFF
   -DKAN_BUILD_TOOLS=OFF` (the `apps/` and `tools/` executables are
   developer tools, out of the release build), same baseline and warning
   policy as the bridge, then `ctest`.
6. kan!'s C# with warnings as errors (the `make check` gate), then the
   `core` and `forge` test suites. NuGet's low/moderate advisories
   (NU1901, NU1902) stay non-fatal; NU1903 and NU1904 (high, critical)
   fail the build (audit C6). The version stamp reaches the three kan
   assemblies (audit C8). No audio device is touched (the live
   tests use a `FakeMic`). `KAN_ROFORMER_GGUF` points at a stub file so the
   ~200 MB RoFormer model is never downloaded during tests; probes fail fast
   and every caller answers with a controlled error. `SeparationTest` (real
   separation) is excluded here: it needs the real model and minutes of
   CPU; run it with `make test` locally.
7. `dotnet publish osu.Desktop` self-contained for the RID, then the kan!
   payload is collected and **checked**: `osu.Game.Rulesets.Kan.dll`,
   `Kan.Forge.dll`, `Kan.Core.dll`, the native bridge (`.dll`/`.dylib`/
   `.so`, whatever the CMake build produced), `ppy.ManagedBass.dll`, the
   `brand/` folder (the fork copies it, F-21), the
   `THIRD_PARTY_NOTICES.md` + `LICENCE` files (readable credits in every
   zip, docs/release-credits.md GAP 8) and the `licenses/` folder of
   licence texts (GAP 9). Symbols are kept OUT of the payload: the
   portable PDBs (with local build paths) stay in the build output for
   developers (audit C9). The job fails if anything is missing. These
   steps live in `game/packaging/windows/assemble.sh` (not only in YAML)
   so a local build produces the identical payload.
8. The visible licence gate (audit C3): `game/packaging/check-licenses.sh`
   re-checks the final payload against the one list
   `game/packaging/licenses.manifest`, so a release cannot ship with an
   incomplete `licenses/` folder.
9. macOS only: assemble `kan!.app` (see §3), sign + notarize when the
   `APPLE_*` secrets exist (the secret values reach only the signing
   step; a job-level boolean says whether they exist, audit C5).
10. Zip (`kan-<rid>.zip`; on macOS the zip contains `kan!.app` plus the
   notices, licence and `licenses/` at the zip root), upload as a build
   artifact. On `v*` tags all four zips are attached to a **draft**
   GitHub Release (`gh release create --draft`).

Version: tag builds stamp `-p:Version=<tag without v>` on the kan
assemblies and `-DKAN_VERSION` on the native bridge; other builds are
`0.0.0`.

Workflow actions are pinned by commit SHA with the release in a comment
(audit C5); runner images still float (documented here as the known
remainder).

### What CI cannot check

- The game actually starting, drawing, playing sound, or hearing the
  microphone on Windows/macOS (runners have no audio stack to listen to).
- Code signing/notarization without the Apple secrets.
- The `apps/` and `tools/` executables outside Linux.

## 3. macOS app bundle (`kan!.app`)

Upstream osu!lazer does its macOS packaging outside its repository
(Velopack/osu-deploy); there is no bundle template in the fork to reuse, so
kan! assembles its own (fork-divergence F-23: no fork change was needed).
Layout produced by the workflow:

```
kan!.app/
  Contents/
    Info.plist            game/packaging/macos/Info.plist (%%VERSION%% → version)
    MacOS/                the self-contained publish output (kan + everything)
    Resources/kan.icns    from game/client/assets/lazer.png (sips + iconutil)
```

`Info.plist`: bundle id `org.kairophos.kan`, name `kan!`, executable `kan`,
and, **the reason this file exists at all**,
`NSMicrophoneUsageDescription` = "kan! listens to your instrument to judge
your playing. Audio never leaves your computer." Without that key macOS
**silently feeds silence to the microphone** instead of prompting.

`kan.entitlements` (hardened runtime, applied at signing):
`com.apple.security.device.audio-input` (the microphone) and
`com.apple.security.cs.allow-jit` (CoreCLR compiles IL with a JIT, which the
hardened runtime forbids otherwise).

### Signing and notarization (not set up yet)

Needs a **paid Apple Developer account**. When these repository secrets
exist, the workflow signs and notarizes automatically; when they are absent
the step is skipped and the zip is unsigned:

| Secret | Contents |
|---|---|
| `APPLE_CERTIFICATE` | Developer ID Application certificate, `.p12`, base64 |
| `APPLE_CERTIFICATE_PASSWORD` | password of that `.p12` |
| `APPLE_SIGNING_IDENTITY` | e.g. `Developer ID Application: …` |
| `APPLE_ID` | Apple ID email |
| `APPLE_PASSWORD` | app-specific password for that Apple ID |
| `APPLE_TEAM_ID` | team id |

Signing order: every native `.dylib`/`.so` inside the bundle first, then the
bundle with `--options runtime --entitlements kan.entitlements`, then
`notarytool submit --wait` on a zip and `stapler staple` on the app.

### What an unsigned build means for players

macOS (Gatekeeper) blocks double-clicking apps from the internet that are
not notarized. Players have two one-time ways around it:

1. Right-click `kan!.app` → **Open** → Open (once; afterwards
   double-click works), or
2. `xattr -dr com.apple.quarantine "/Applications/kan!.app"`

The microphone prompt itself works either way (the `Info.plist` key is what
matters there). Say this in the release notes until the builds are signed.

## 4. Windows

### 4a. Local Windows x64 test build (2026-11-09)

Before CI could run, a runnable test build was assembled **on Linux** for
the owner's Windows PC (two scripts, both reproducible):

```bash
game/packaging/windows/cross-build-native.sh   # kan_forge_native.dll in Docker (MinGW-w64)
game/packaging/windows/assemble.sh win-x64 dist/game   # publish + payload (same steps as CI)
```

- **Native bridge:** `game/packaging/windows/cross-build-native.sh` builds
  `game/forge/native` in a `debian:bookworm` + MinGW-w64 container
  (`Dockerfile.mingw`, image kept as `kan-mingw-w64:bookworm`; one `--rm`
  container at a time, `--cpus 6`, `--parallel 6`). CPU-only
  (`KAN_GGML_BACKEND=CPU`, no Vulkan in this build), `GGML_NATIVE=OFF`
  (portable codegen), `GGML_OPENMP=OFF` (no `libgomp-1.dll` dependency),
  C++ runtime linked statically (`-static -static-libgcc
  -static-libstdc++`). Verified every run: imported DLLs are only
  `ADVAPI32/KERNEL32/msvcrt`, and all `kan_forge_*` entry points of
  `KanForgeNative.cs` are exported.
- **Payload:** `assemble.sh` runs the CI payload steps (strict kan! build,
  self-contained `osu.Desktop` publish, ruleset/Kan.Forge/Kan.Core + native
  bridge + `brand/` + `ppy.ManagedBass.dll` + `THIRD_PARTY_NOTICES.md`,
  `LICENCE` and the `licenses/` folder of licence texts, all checked;
  portable PDBs are stripped from the payload) and the zip adds
  `game/packaging/windows/README.txt` (SmartScreen, CPU-only separation,
  `%APPDATA%\kan` for data/logs, how to report). Result:
  `dist/kan-win-x64.zip` (176 MiB, unzip → double-click `kan.exe`).
  `dist/` is gitignored; the zip is handed to the owner, never committed.

Still untested: the game actually running on Windows (see the list in §6).

- **Microphone:** desktop (non-store) apps need no permission; BASS opens
  capture through **WASAPI** (`Bass.RecordInit`/`Bass.RecordStart` in
  `game/forge/Play/LiveSession.cs`, the `MicSource` class). miniaudio is
  **decoder-only** in the game (`ma_decoder_init_file` in
  `game/forge/native/kan_forge_api.cpp`); its capture/playback backends are
  used only by the `apps/` developer tools. The publish output already
  carries the per-RID BASS natives (`bass.dll`/`basswasapi.dll` on win-x64,
  `libbass.dylib` on osx-arm64, verified 2026-11-09 in a cross-publish);
  only real device I/O is untested until CI or a player runs it.
- **Native DLL:** `kan_forge_native.dll` next to `kan.exe` (P/Invoke
  `kan_forge_native` → `kan_forge_native.dll` / `libkan_forge_native.dylib`
  / `libkan_forge_native.so`). The CI payload check asserts it is in the
  zip; `game/core/Kan.Core.csproj` and `game/ruleset/Kan.Ruleset.csproj`
  copy it for local builds (both CMake layouts: `build/` and
  `build/<Configuration>/`).
- **Velopack:** the client is already wired for Velopack 1.2.0
  (`osu.Desktop/Program.cs` `setupVelopack`, `Updater/VelopackUpdateManager.cs`),
  but no installer is produced; the zip is the installer for now. To add
  one later: run `vpk pack` over the publish output on Windows (produces
  `install.exe` + `Setup.exe`), publish those as release assets, and, since
  the updater (F-20) reads releases of `github.com/KairoPhos13/kan`, either
  publish the Windows release there too or point the updater at the repo
  that hosts the releases (a one-line fork change, would be logged as a
  fork entry). Signing the installer for SmartScreen needs a code-signing
  certificate (not the Apple one).
- Windows file/link associations stay off (fork F-20) and would need kan!'s
  own file types first.

## 5. Linux

The only platform with a verified build and run (`game/docs/environment.md`).
The CI job builds the same way (GCC, Vulkan when the SDK is installed).
BASS talks to ALSA/PulseAudio/PipeWire as before.

## 6. Platform audit: Linux-only or untested

Scope: kan-owned code (`game/core`, `game/forge`, `game/ruleset`,
`game/tools`, `game/Makefile`, plus `libkan`, `apps/`, `tools/` where the
game build touches them). The fork (`game/client`) is upstream osu!lazer,
which ships on all three platforms, so its deltas were only checked for
kan!-added platform assumptions (none found; see F-23 in
`fork-divergence.md`). Grepped for: `/proc`, `/tmp`, `/dev`, `~/.`,
`Environment.OSVersion`, `RuntimeInformation`, `OperatingSystem.Is*`,
`.so`/`.dylib`/`.dll` by name, path separators, `ALSA`/`PulseAudio`/`xdg`,
`popen`/`system` shell calls, `chmod`, `setenv`, `mkdir`, `unistd.h`,
case-sensitive file names.

Status: **fixed (2026-11-09, `agent/ci`)** = changed in this pass; **proposed**
= safe fix identified but not made here; **by design / info** = no change
wanted or needed.

### Fixed in the Windows test-build pass (2026-11-09, later)

Producing a runnable Windows x64 zip locally (§4a) surfaced these; all
fixed here:

| Where | What | Risk it removed |
|---|---|---|
| `libkan/include/libkan/transcribe.hpp:155-166` | model auto-download probed `command -v curl`/`wget` and quoted with `'`: POSIX shell; `cmd.exe` answers "not found" and treats `'` as literal | Windows: the ~200 MB RoFormer model was never auto-downloaded; now `where curl.exe` (Windows 10 1803+) + `curl.exe` with `"` quoting (`shell_quote`/`curl_download_cmd`, unit-tested by `libkan_transcribe_cmd_test`) |
| `libkan/include/libkan/transcribe.hpp:238-253` | `find_audio_downloader` probed `command -v yt-dlp`, `test -x "$HOME/…"`, `python3 -m yt_dlp` | Windows: "fetch audio from URL" found no downloader; now `where yt-dlp.exe`/`youtube-dl.exe`, `py -m yt_dlp`, `python -m yt_dlp` |
| `libkan/include/libkan/transcribe.hpp:82` | `command -v stdbuf` probe ran in `cmd.exe` (noise; `/dev/null` is not a device there) | gated `#ifndef _WIN32`; Windows children write progress unbuffered anyway |
| `libkan/CMakeLists.txt` (ggml pin block) | pinned ggml's CPU backend uses `THREAD_POWER_THROTTLING_STATE`/`ThreadPowerThrottling` (Windows 10 RS3 SDK types) which mingw-w64 headers (≤ 12.x) do not declare | MinGW cross build of the native bridge failed on unknown types; `_WIN32_WINNT=0x0601` on `ggml-cpu` when `MINGW` compiles the opt-out away (scheduling nicety only; MSVC keeps it) |
| `game/forge/native/CMakeLists.txt` | MinGW names the DLL `libkan_forge_native.dll`; P/Invoke looks for `kan_forge_native.dll` | Windows: native bridge not found; `PREFIX ""` on `WIN32` |
| `.github/workflows/build.yml` strict-build step | `-p:WarningsNotAsErrors="NU1901;…"`: a raw `;` splits MSBuild properties (MSB1006); the Makefile's `STRICT` uses `%3B` | CI's kan! C# gate would have failed on every job; now `%3B` |

### Fixed in the first pass

| Where | What | Risk it removed |
|---|---|---|
| `game/forge/native/kan_forge_api.cpp:146-149` | POSIX `setenv` (MSVC has none) | Windows build failure of the native bridge |
| `game/forge/native/kan_forge_api.cpp:96-107` | fetch command quoted with `'` through `std::system`: `cmd.exe` has no single-quote quoting | Windows: "fetch audio from URL" silently broken |
| `game/forge/native/CMakeLists.txt:39-64` | `-Wall -Wextra` passed to MSVC; ggml backend forced Vulkan on every platform | MSVC warning noise / wrong backend; now `KAN_WARNING_FLAGS` (`/W4` on MSVC) + `KAN_GGML_BACKEND` + `_USE_MATH_DEFINES` |
| `libkan/CMakeLists.txt:175-215` | `GGML_VULKAN` forced ON even without Vulkan tools, no Metal path | configure failures / no GPU on macOS; now `KAN_GGML_BACKEND` (AUTO→Metal/Vulkan/CPU) |
| `libkan/tests/fixture_gen.cpp:25-31` | `::mkdir(dir, 0755)` (POSIX signature) | MSVC build failure of the libkan tests |
| `game/core/Kan.Core.csproj:16-38` | native library looked up only in `build/` | Visual Studio builds (output in `build/<Configuration>/`) never shipped the DLL to tests/game |
| `game/ruleset/Kan.Ruleset.csproj:40-45` | deploy copy skipped `kan_forge_native.dll` | Windows client output without the native bridge |
| `game/core/tests/NativeBridgeSmokeTest.cs:41-52` | `/tmp/...` path through the native bridge; assertion expected `bs_roformer` in an error message that now says "separator runtime" | tests fail on Windows; stale assertion fails wherever no separator is found |
| `game/forge/tests/SeparationTest.cs:49-54` | `/tmp/...` paths through the native bridge | same (now `Path.GetTempPath()`) |

### Fixed in the release-hardening pass (2026-11-12)

From the November 2026 audit (section 7, `agent/release-hardening` lane):

| Where | What | Risk it removed |
|---|---|---|
| `libkan/cmake/kan_build_policy.cmake` (new), `game/forge/native/CMakeLists.txt`, root `CMakeLists.txt` | ggml tuned to the build host's CPU (GGML_NATIVE default ON; with it OFF, AVX2-era kernels still on) and root `-march=native` | shipped `kan_forge_native` dying with "illegal instruction" on older player CPUs; now a fixed baseline (x86-64-v2 / arm64; `KAN_MARCH_NATIVE=ON` is the local opt-in) |
| `game/packaging/licenses.manifest` (new), `game/packaging/check-licenses.sh` (new), `assemble.sh`, `build.yml` | release archives carried no licence texts | Apache-2.0 and LGPL redistribution obligations unmet; now `licenses/` in every archive from ONE list, CI-checked |
| `libkan/include/libkan/transcribe.hpp`, `libkan/include/libkan/sha256.hpp` (new) | separation model from `/resolve/main/`, no hash check | a changed or truncated ~200 MB model parsed blindly; now pinned revision + SHA-256, wrong hash deletes the file calmly |
| `.github/workflows/*.yml` | actions on floating tags; signing secrets at job level | tag movement / third-party action drift; secrets reach only the signing step |
| `game/forge/native/CMakeLists.txt`, `game/core/tests/StarterPackTest.cs` | MSVC runtime needed on player machines; starter-pack test passed silently at zero levels | static CRT in the DLL; zero levels now fails |
| `game/Makefile`, `game/packaging/windows/assemble.sh` | `make check` failed in a worktree where `game/client` is a symlink (the agent worktree model): the fork built through its physical path while the kan projects reached it through the link, and MSBuild's incremental clean compares paths as strings, so `osu.Game`'s reference assembly was deleted right after it was written (`CS0006` in every consumer); entering the fork by path spelling instead lost NuGet's package flow (`osu.Framework` unreferenced) | the fork legs build `cd`-entered (physical path) with their own MSBuild output bookkeeping (`-p:CleanFile=FileListAbsolute.fork.txt`), the kan legs keep the default list; in a normal checkout all spellings are the same directory and nothing changes |
| `assemble.sh`, `Info.plist`, `Dockerfile.mingw`, docs | PDBs with build paths in the zips; `LSMinimumSystemVersion` 13.0 contradicting its own comment; floating Docker base; docs claiming more than tests assert | symbols out of the payload; 14.0 (the .NET 10 supported floor, Microsoft's macOS support table); digest-pinned base image; doc claims aligned with the tests and `TEST_LOG.md` |

### Proposed (not changed here)

| Where | What | Risk | Fix |
|---|---|---|---|
| `game/Makefile:57-62` | `deploy` target checks `libkan_forge_native.so` only | `make deploy` fails on a macOS/Windows toolchain even when the build is fine | accept any of the three names (`test -f` over a list) |
| `game/forge/native/kan_forge_api.cpp:112` | `std::system` shell-out for yt-dlp (quoting fixed above) | paths containing `%` would still be expanded by `cmd.exe` | quote/escape `%` on `_WIN32`, or spawn the process without a shell |
| `apps/kan_demo/main.cpp:856`, `tools/chart_scribe/main.cpp:134` | `std::system` shell-outs with POSIX quoting (ffmpeg/yt-dlp pipelines) | dev tools: broken on Windows as shipped | out of the game's release scope; fix with the tools lane |
| `apps/kan_cli/main.cpp`, `apps/kan_demo/main.cpp` | miniaudio capture/playback device code (`ma_device`) | never built or run on Windows/macOS (CI sets `KAN_BUILD_APPS=OFF`) | enable `KAN_BUILD_APPS` in a CI job once someone wants the tools there |

### By design / informational

| Where | What |
|---|---|
| `game/core/Native/KanForgeNative.cs:90` | `DllImport("kan_forge_native")`: .NET maps it to `kan_forge_native.dll` / `libkan_forge_native.dylib` / `libkan_forge_native.so`; all three ship next to the executable (checked by CI) |
| `game/forge/Play/LiveSession.cs:35` (`MicSource`) | capture is **BASS**, not miniaudio: WASAPI on Windows, CoreAudio on macOS (needs the `NSMicrophoneUsageDescription` + audio-input entitlement, §3), ALSA/Pulse on Linux |
| `game/forge/Media/MediaAudioExtractor.cs:42` | `Bass.Init(-1, …)`: BASS's "no sound device" init: decoding works headless (tests, CI) |
| `libkan/src/roformer_runtime.cpp:38-45` | `set_env` already shims `_putenv_s`/`setenv`, portable |
| `game/core/tests/TrackRolesTest.cs:23` and friends (`MixdownRuleTest`, `TrackTimelineTest`, `TrackAnalysisTest`, `TrackEditsTest`, `MediaExtractionTest`, `RenderMixTest`) | `/tmp/...` strings are **fixture data**; they never touch the filesystem, so they work on every OS (left as-is; grep keeps finding them) |
| `game/brand/` + `game/brand/README.md` | resource names (`Textures/Menu/logo.png`, …) are matched **case-sensitively** by osu!framework's resource store on every OS; the README table is the contract; a case typo costs a fallback to osu!'s art on macOS/Windows too |
| `game/client/osu.Desktop/MacOS/MacOSAppLocationChecker.cs` | upstream: warns when the app is outside `/Applications`, intended behaviour for the bundle |
| `osu.Desktop/Updater/VelopackUpdateManager.cs:59` (fork, F-20) | updater reads releases of `github.com/KairoPhos13/kan`, while this workflow publishes releases of `kan-rhythm`; pick one (see §7) |

### Untested until CI (or a human) runs it

- The whole Windows/macOS build: MSVC + ggml-vulkan, Metal + ggml, BASS
  natives for `osx-arm64`/`osx-x64`/`win-x64` from `ppy.osu.Framework.NativeLibs`.
- The `kan!.app` bundle (icon, plist keys, mic prompt) on real macOS.
- Codesign/notarization script (no Apple account here).
- `dotnet publish -r osx-arm64`'s cross-publish of managed code **was**
  verified on this Linux machine (§8); the native parts are not.

## 7. Open questions for the owner

1. **Push access.** The build machine has no GitHub credentials: the fork
   commits pinned by the submodule (see `fork-divergence.md` push blocker)
   and this `agent/ci` work must be pushed by hand before CI can run at all
   (CI clones both repositories). Both repos go public at the same time.
   The workflow's submodule step doubles as the pre-push check the
   November 2026 audit (C1) suggests: CI fails at "Fetch submodules over
   HTTPS" whenever the pinned `game/client` commit is not reachable on the
   fork's remote, so a parent push with a dangling pin cannot go green.
2. **Apple Developer account** (paid) + the six `APPLE_*` secrets for signed
   and notarized macOS builds. Until then ship unsigned (§3).
3. **Where releases live.** This workflow attaches zips to `kan-rhythm`
   releases; the in-game updater (F-20) looks at `KairoPhos13/kan`. Decide
   whether to mirror releases there or repoint the updater.
4. **Velopack installers** (Windows `install.exe`, macOS zip-updates): out of
   scope for now (§4), needs `vpk` in CI and a signing certificate.
5. **`LSMinimumSystemVersion`** (resolved 2026-11-12): 14.0 in
   `Info.plist`, the floor of the .NET 10 supported macOS set
   (Microsoft's "Install .NET on macOS" support table lists macOS 14, 15
   and 26; checked 2026-11-12). It was 13.0 before, below that floor and
   contradicting its own comment (audit C9). Promise less only if the
   product deliberately supports macOS versions Microsoft does not.

## 8. Local verification performed (2026-11-09, Linux x86_64)

All builds bounded (`nice -n 10`, `--parallel 6`, `JOBS=6`,
`-maxcpucount:4`). Commands and outcomes:

1. **Native bridge with the new CMake logic** (backend `AUTO` resolved to
   Vulkan here; configure log: "Including Vulkan backend", ggml 0.26.0 pin
   `3612c0af`):

   ```bash
   cmake -S game/forge/native -B game/forge/native/build -DCMAKE_BUILD_TYPE=Release
   cmake --build game/forge/native/build --parallel 6
   ctest --test-dir game/forge/native/build --output-on-failure
   ```

   → clean build, **ctest 2/2** (`kan_forge_selftest` 0.9 s, `score_error`
   73 s).

2. **Root build: the owner's daily build** (default options, `apps/` +
   `tools/` included):

   ```bash
   cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
   cmake --build build --parallel 6
   ctest --test-dir build --output-on-failure
   ```

   → clean build, **25/25 tests passed** (99 s total).

3. **`make check` in `game/`** (`JOBS=6`; `KAN_ROFORMER_GGUF` pointed at an
   already-downloaded 6-stem model so nothing was re-fetched):

   ```bash
   cd game && make check JOBS=6
   ```

   → green: native bridge + self-test (ctest 2/2), strict builds of
   core/forge/ruleset (`-warnaserror`), `Kan.Core.Tests` **130/130**,
   `Kan.Forge.Tests` **48/48** (including the real `SeparationTest`
   end-to-end separation).

4. **Cross-publish of the managed projects** (the mission's required check):

   ```bash
   dotnet publish game/client/osu.Desktop/osu.Desktop.csproj -c Release -r win-x64 \
     --self-contained true -p:Version=0.0.0 -maxcpucount:4 -o <out>/win-x64
   dotnet publish game/client/osu.Desktop/osu.Desktop.csproj -c Release -r osx-arm64 \
     --self-contained true -p:Version=0.0.0 -maxcpucount:4 -o <out>/osx-arm64
   ```

   → both succeed. Payload inspected: `kan.exe` / `kan` apphost,
   `osu.Game.dll`, `brand/` (12 files), `ppy.ManagedBass.dll` and the
   per-RID BASS natives (`bass.dll` + `basswasapi.dll` on win-x64,
   `libbass.dylib` on osx-arm64). This inspection found and fixed the CI
   payload check: the managed BASS assembly is `ppy.ManagedBass.dll`, not
   `ManagedBass.dll`.

5. **No-separator code path** (what Windows CI sees without a model):
   `dotnet test game/core/tests --filter NativeBridgeSmokeTest` with
   `KAN_ROFORMER_GGUF` unset and a dead `KAN_MODEL_URL` → **5/5 passed**
   (exercises the fixed `Does.Contain("separator")` assertion).

6. **Workflow YAML**: `npx --yes @action-validator/cli
   .github/workflows/build.yml` → exit 0 (valid). `actionlint` itself is
   not installed on this machine and `go install` is disallowed, so
   action-validator is the lint used.

**Not verified here:** anything that only exists on Windows/macOS (MSVC and
Metal configurations, Visual Studio's multi-config CMake layout,
`sips`/`iconutil`, `codesign`/`notarytool`), the workflow end-to-end on
GitHub Actions, and the game actually running on those platforms.
