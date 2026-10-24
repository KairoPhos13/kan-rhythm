# game/

The **kan!** game: [osu!lazer](https://github.com/ppy/osu) (MIT) forked and
rebranded ([ADR-0010](../docs/adr/0010-osu-lazer-base-client.md),
ADR-0011), plus kan!'s own game mode and level editor. You play a level's
score on a real instrument; the microphone is described live by
[libkan](../libkan/) and the game judges each note by its timing and its
pitch. Integration architecture:
[docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).

## Projects

| Folder | Assembly | What it is | Depends on |
|---|---|---|---|
| `core/` | `Kan.Core` | What a level is and how a play is judged: the score and design models, the live judge, timing, tracks and their edits, transcription drafts, the bridge to libkan. No osu! inside. | libkan (native) |
| `forge/` | `Kan.Forge` | **kan!forge**, the level editor's own tabs (tracks, compose, design), the audio played along a level, the media pipeline (separation, transcription, rendering). | `Kan.Core`, the fork |
| `ruleset/` | `osu.Game.Rulesets.Kan` | The **kan!** game mode: hit objects, the live play (listening, judging, the staff), mods, scoring, replays. | `Kan.Core`, `Kan.Forge`, the fork |
| `client/` | — | The fork of osu!lazer (git submodule). Every change to it is listed in [docs/fork-divergence.md](docs/fork-divergence.md) and kept minimal: most are hooks for the ruleset to add its screens. | — |
| `forge/native/` | `libkan_forge_native` | A thin C ABI over libkan (`kan_forge_api.h`), the only way the game calls it. | libkan |

Each folder has its own README.

## Build, test, run

Everything is defaulted in the `Makefile` (no flags to remember):

```bash
make            # native bridge, core, forge, the client, the ruleset, deployed next to the client
make check      # before a commit: kan's C# built with warnings as errors, then every test
make test       # the native self-test and both C# suites (core's needs no osu! build)
make run        # launch the client
```

Requirements and platform notes: [docs/environment.md](docs/environment.md).
On a machine that has to stay responsive, bound the native build:
`make JOBS=6`.

## Documentation

- [docs/gameplay.md](docs/gameplay.md) — how a play works and is judged (at
  a glance first, then each rule with the reason behind it).
- [docs/editor-pipeline.md](docs/editor-pipeline.md) — the editor: tracks,
  separation, transcription, compose, design, timing repair.
- [docs/fork-divergence.md](docs/fork-divergence.md) — every change to osu!.
- [docs/environment.md](docs/environment.md), [docs/bass-licensing.md](docs/bass-licensing.md).
- Measurement rules libkan keeps (what the game may rely on):
  [../docs/architecture/measurement-invariants.md](../docs/architecture/measurement-invariants.md).
- Work still open: [TODO.md](TODO.md).

## Developer tooling

`ruleset/Dev/KanDevPlay.cs` drives the game unattended. It is inert unless
`KAN_DEV_PLAY` is set (to a level's set id), so it never changes a normal
play. With it: `KAN_LIVE_INPUT` (a recording plays the role of the
microphone; `KAN_LIVE_INPUT_OFFSET_MS` aligns it), `KAN_DEV_MODS`,
`KAN_DEV_SCREEN` (`compose`, `design`, `setup`, `tracks`, `replay`, …),
`KAN_DEV_REPAIR` (scripted editor steps), `KAN_DEV_SCREENSHOTS`,
`KAN_DEV_PAUSE`, `KAN_DEV_RESTART`. The class comment lists them all.

Test runs belong in an isolated instance (a headless compositor, a copy of
the game's data, volume 0, its own network namespace): a developer's own
game and microphone are never touched. A test instance never opens the
microphone — without `KAN_LIVE_INPUT` it listens to nothing.

## Ground rules

- English only ([CONTRIBUTING.md](../CONTRIBUTING.md)).
- **Minimal fork divergence**: prefer ruleset code over fork edits; record
  every fork change in `docs/fork-divergence.md`.
- **libkan describes, the game judges**: libkan reports what it hears;
  verdicts are made here, in `Kan.Core` (`LiveJudge`).
- **License compliance is ship-blocking** (ADR-0010): keep upstream headers
  and license text, ship `THIRD_PARTY_NOTICES`, resolve BASS licensing
  before any donation-driven release.
- Real-time safety: audio capture and description stay in libkan (C++);
  the game's own audio is music and effects.
