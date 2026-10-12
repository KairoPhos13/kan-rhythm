# game/ client — build environment & workflow

Verified working environment for the **kan!** client (`game/client/` =
osu!lazer fork `github.com/KairoPhos13/kan`, plus the `game/ruleset/` plugin).
All facts below verified on **2026-10-12** on the working machine
(Arch Linux, x86_64, 12 cores, 32 GB RAM, AMD RX 6800, X11 session).

**Windows/macOS: UNVERIFIED.** osu!lazer itself supports all three platforms
natively, so the same commands are expected to work, but nobody has built or
run the client there yet. When you verify a platform, move it out of this
paragraph and record the results below.

## 1. Toolchain

| Tool | Version used | Notes |
|---|---|---|
| .NET SDK | **10.0.401** (`10.0.x` channel) | The fork's `global.json` pins `10.0.100` with `rollForward: latestFeature`, and projects target **`net10.0`**. A .NET 8 SDK cannot build this fork — earlier plans mentioning "dotnet 8" are superseded by the upstream pin. |
| NuGet | bundled with SDK | Network access required on first restore (~140 packages; osu!framework + resources are large). |
| CMake + GCC/Clang (C++20) | CMake 4.x, system compiler | Only for the C++ side (`libkan/`, `kanfx/` — root `CONTRIBUTING.md` §4). Not needed to build the client. |
| Python 3 | 3.14 | Only for `game/tools/gen-third-party-notices.py`. |
| Platform packages (Linux) | Mesa GL 4.6, X11 | SDL3 natives ship inside `ppy.osu.Framework` (NuGet) — no system SDL needed. Audio uses the default device (PipeWire/PulseAudio/ALSA). Optional `xdg-utils` warnings (`qtpaths: command not found`) are harmless. |

### Installing the .NET SDK without root

On this machine system packages need a sudo password, so the SDK is
user-local:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
```

`DOTNET_ROOT` matters at **run** time too: the `kan` apphost locates the
runtime through it. Without it you get
`You must install .NET to run this application` even though `dotnet build`
worked.

## 2. Getting the sources (submodule)

`game/client/` is a git submodule of `github.com/KairoPhos13/kan` (see
`.gitmodules`). After cloning the monorepo:

```bash
git submodule update --init game/client
```

The submodule URL is SSH (`git@github.com:...`). If you have no GitHub SSH
key configured, switch to HTTPS for this checkout first (read access is
public):

```bash
git config submodule."game/client".url https://github.com/KairoPhos13/kan.git
git submodule update --init game/client
```

The pinned commit is recorded in `game/docs/fork-divergence.md` (pin log).
Changes to the fork are made **in the submodule** and committed there; the
monorepo records the resulting pin movement.

## 3. Build commands

Baseline (unmodified upstream surface) — build the fork's desktop client:

```bash
cd game/client
dotnet build osu.Desktop/osu.Desktop.csproj -c Release
```

Build the kan! ruleset plugin (compiles **against** the fork, never inside
it — `game/ruleset/Kan.Ruleset.csproj` references
`../client/osu.Game/osu.Game.csproj`):

```bash
cd game/ruleset
dotnet build -c Release
```

A post-build step copies `osu.Game.Rulesets.Kan.dll` next to the client
executable automatically (development convenience).

Run the client:

```bash
cd game/client/osu.Desktop/bin/Release/net10.0
DOTNET_ROOT=$HOME/.dotnet ./kan
```

### Measured timings (2026-10-12, 12 cores)

| Step | Time |
|---|---|
| `dotnet restore` of the fork (NuGet cache already warm) | ~21 s |
| First-ever restore on a clean machine | several minutes (large packages: osu!framework natives, game resources) |
| `dotnet build osu.Desktop -c Release` (restore included) | **1 min 04 s** (0 warnings, 0 errors) |
| Same build after the rebrand deltas (incremental) | 45 s |
| `dotnet build` of `game/ruleset` (warm) | ~2 s |
| First game launch | slower (shader compilation); later launches ~10 s to interactive |

## 4. Where the ruleset plugin is loaded from

The fork's ruleset loader (`osu.Game/Rulesets/RulesetStore.cs`) discovers
DLLs matching **`osu.Game.Rulesets.*.dll`** in:

1. the application startup directory (`osu.Desktop/bin/<Config>/net10.0/`),
   or
2. the game's `rulesets/` storage folder
   (Linux: `~/.local/share/kan/rulesets/`; user data root is named `kan`
   since the rebrand — `osu.Desktop/Program.cs` `base_game_name`).

Consequences (see also `game/docs/fork-divergence.md`):

- The **assembly file name** must be `osu.Game.Rulesets.Kan.dll` (upstream
  loader constraint). The C# **namespaces** are `Kan.*` per ADR-0011 — only
  the file name carries the upstream prefix.
- If the plugin fails to load, the client logs
  `An issue with ruleset "..." occurred` at error level.

## 5. Tests

- The fork's test suite is `osu.Desktop.slnf`-scoped and runs headless when
  no `DISPLAY` is set (this is how upstream CI runs it on Linux):

  ```bash
  cd game/client
  env -u DISPLAY dotnet test osu.Desktop.slnf -c Release -- NUnit.ConsoleOut=0
  ```

- **Measured 2026-10-12** (Release, headless, .NET SDK 10.0.401):
  `osu.Game.Tests` completed — **5481 passed / 9 failed / 23 skipped**
  (5513 total, 16 m 20 s). Re-running the 9 failures twice showed:
  2 are flaky (spectator `TestClientSendsCorrectRuleset`, editor
  `TestRestart` — pass on rerun); 7 fail deterministically (4× chat-link
  action mapping in `TestSceneChatLink`, `BeatmapImporterTests
  .TestImportFailsWithFilenamesDifferingOnlyInCase`, `TestSettingsMigration
  .TestDisplayStarsMigration`) — none of these code paths intersect the
  kan! deltas (see `fork-divergence.md`), pending verification against a
  pristine `816df139fd` build (`game/TODO.md`).
  Full result file: `game/client/osu.Game.Tests/TestResults/*.trx` (not
  committed).
- After `osu.Game.Tests`, the `dotnet test osu.Desktop.slnf` run aborts while
  starting the ruleset test projects with a
  `Microsoft.CodeCoverage.targets MSB4181` tooling error (SDK 10.0.401).
  Running test projects individually (`dotnet test osu.Game.Rulesets.Osu.Tests/...`)
  is the workaround; tracked in `game/TODO.md`.
- The kan ruleset currently has no test project (Phase 0 skeleton). The DSP
  tests live in `libkan/tests/` (root `CONTRIBUTING.md` §4) and are unrelated
  to this component.

## 6. Verification performed on this machine (2026-10-12)

1. **Baseline build green** — command and timing above; the unmodified fork
   built with 0 warnings / 0 errors.
2. **Running "kan!" window** — the client launches on this X11 desktop
   (SDL3 / GL 4.6 / BASS 2.4.17 per `~/.local/share/kan/logs/*.runtime.log`);
   window title reported by `xdotool getwindowname` is exactly **`kan!`**.
3. **Ruleset plugin path** — the runtime log contains
   `kan! ruleset loaded (Phase 0 skeleton — judgment core pending)`, emitted
   once when the fork's loader instantiates `KanRuleset`; the ruleset icon
   appears in the in-game toolbar.
4. **About screen & THIRD_PARTY_NOTICES** — compiled into `osu.Game`
   (`KanAboutSettings` under Settings → General) and the notices text is
   verified present as an embedded resource of `osu.Game.dll`. (Interactive
   rendering of the about screen has not been screenshot-verified yet.)

## 7. License verification notes (from the notices generator)

`game/tools/gen-third-party-notices.py` classifies every package in the
`osu.Desktop` dependency closure. Hard findings inherited from upstream
osu!lazer (LGPL-family, dynamic linking only — full list in
`game/client/THIRD_PARTY_NOTICES.md` §4):

- `TagLibSharp` (LGPL-2.1-only), `OpenTabletDriver*` (LGPL-3.0-or-later),
  `FFmpeg.AutoGen` (wrapper; FFmpeg itself is LGPL/GPL and is **not**
  redistributed by us — users supply binaries).

These do not violate the MIT-only policy for *our own* dependency choices
(`CONTRIBUTING.md` §6) but must be reviewed before any release. Known
permissive statuses verified manually (2026-10-12): `ppy.*` packages
(SDL2-CS, osuTK.NS20, Veldrid, Vk, managed-midi, osu.Game.Resources) are MIT
(upstream ppy forks), `SQLite` is public domain, `StbiSharp` is BSD.
