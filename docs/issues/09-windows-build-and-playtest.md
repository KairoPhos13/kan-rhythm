# Verify the Windows build and play-test kan! on Windows

Labels: `platform:windows`, `game`, `help wanted`
Size: M

## Context

kan! targets Linux, Windows and macOS natively
([README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md)),
but only the Linux build is verified today: "Windows and macOS builds are
unverified" ([game/README.md — Status](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md))
and [game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md)
lists "Windows build and installer verified" as important for a first
impression. The game is a .NET 10 fork of osu!lazer with a native C++
bridge (`game/forge/native/`) — both need to build and work on Windows.

One deliberate difference to verify: since fork F-20, kan! **does not**
install Windows file/link associations (they were osu!'s and would be
stolen from an installed osu!) — see
[game/docs/fork-divergence.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/fork-divergence.md).
The updater reads github.com/KairoPhos/kan's releases.

## What to do

1. Build on Windows (same commands as
   [game/docs/environment.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/environment.md);
   .NET 10 SDK + CMake + a C++ toolchain for the native bridge). Record
   tool versions, commands, build warnings (target: 0 warnings / 0
   errors) and the run result.
2. Play-test: launch the client, complete the first-run flow if present,
   play one easy level with a real instrument and microphone, use the
   editor's track tab once, and check the about screen
   (Settings → General → About kan!) shows the kan! identity and
   `THIRD_PARTY_NOTICES`.
3. Verify the deliberate behaviours:
   - no `.osz`/`.osr`/`.osk`/`osu://` associations are registered;
   - the updater checks github.com/KairoPhos/kan, not osu!;
   - kan!'s brand resources (logo, menu backgrounds) load, app icon is
     kan!'s provisional one;
   - no request to any ppy.sh host in the logs (F-20).
4. If a Windows installer/release packaging exists or is attempted,
   verify install, upgrade and uninstall (uninstall must not remove
   anything belonging to an installed osu!). How releases are built and
   hosted is a maintainer decision — this issue covers verification and
   findings.
5. Record results in `game/docs/environment.md` and file bugs as
   separate issues.

## Acceptance criteria

- [ ] Build green on Windows: 0 warnings / 0 errors, versions recorded
      in `game/docs/environment.md`
- [ ] Client runs: one easy level played end to end with a microphone
- [ ] About screen and `THIRD_PARTY_NOTICES` verified visually
- [ ] No file/link associations registered (verified in the registry or
      by double-clicking an `.osz`)
- [ ] Logs show no ppy.sh traffic
- [ ] Findings filed as separate issues; environment.md updated

## Pointers

- [game/docs/environment.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/environment.md) —
  toolchain and command record (extend it)
- [game/docs/fork-divergence.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/fork-divergence.md) —
  F-20 (offline, no associations), F-21/F-22 (brand, icons)
- [game/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md) —
  `make` targets; on Windows run the underlying `dotnet`/`cmake` commands
  it wraps
- `game/tools/gen-third-party-notices.py`, `game/tools/gen-provisional-brand.py`

## Notes

- Test instances must stay isolated (own data copy, volume 0 — see
  "Developer tooling" in `game/README.md`); never point test runs at a
  player's real game data or osu! install.
- Signing/notarisation of release binaries is a pending maintainer
  decision; a locally built binary is what this issue verifies.
