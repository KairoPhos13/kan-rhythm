# game/: the kan! game

**kan!** (from the Japanese *kanaderu*, 奏でる, "to play an instrument")
is a rhythm game you play on a real instrument. A level is a song's
score: notes scroll along a staff toward a line, and you play each one on your
own instrument as it reaches the line. The microphone is all it needs. Each
note is judged by its timing and its pitch together: perfect, good, meh or
miss.

The game is a fork of [osu!lazer](https://github.com/ppy/osu) (MIT), with
kan!'s own game mode and level editor
([ADR-0010](../docs/adr/0010-osu-lazer-base-client.md), ADR-0011). Live
listening is [libkan](../libkan/)'s: it describes what it hears, and the game
judges it. Integration architecture:
[docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).

## What it does

**Play**

- **Any instrument that plays notes.** The level's notes are fitted to your
  instrument's range, and its own tuning is learnt and taken off: a slightly
  flat or sharp instrument is not punished. So far it has been played mostly
  on an alto ocarina.
- **Forgiving by default.** Timing windows follow the music's pace (how close
  the notes are), not its tempo. A note in tune and on time is a perfect; one
  a little out on either is still a hit. Strictness is opt-in, with mods.
- **The staff.** One scale for the whole level (an option zooms passage by
  passage). Notes are named as letters or Do-Re-Mi, or not at all, and can
  each have their own colour.
- **Mods for learning:** *Wait for me* stops at each note until it is played,
  *Practice* loops a section, *By heart* shows each note only for a moment,
  *Half Time* slows the level. Easy, Hard Rock, Hidden, Sudden Death,
  Perfect, Double Time and Auto work as in other rhythm games.
- **Every level at one loudness.** The level audio is rendered at a reference
  loudness (-20 LUFS), so no level is much louder than the next; each can
  still be set louder or softer.
- **Replays**, a song-select preview, and a trace of each play.

**Make levels (kan!forge, in the editor)**

- **Tracks:** import a song, separate it into instruments on your own
  computer, and choose what plays along with you (the companion). Cut, move
  and fade, with undo.
- **Compose:** a piano roll to write the score, or to correct a draft
  transcribed from the song. Click a key to hear its note. Tempo grid with
  sync points placed where you choose.
- **Design:** the level's look while it is played.
- **Verify:** a checklist before sharing a level: notes the instrument
  cannot reach, two notes at once on a one-note instrument, guessed pitches,
  notes past the end of the audio, level audio out of date or off its
  loudness.

Everything runs locally: no account or connection is needed to play or make
levels, and nothing heard by the microphone is recorded or sent.

## Status

Playable and in daily testing, close to its first public release.

- **Identity:** kan! has its own provisional logo, icons and menu art
  ([brand/](brand/README.md)); the osu! welcome voice and intro music are
  off. Some sounds and on-screen texts (other languages above all) still
  come from the osu!lazer base and are being replaced.
- **Online:** optional. kan! has its own small server
  ([server/](../server/)) for accounts, downloading levels and
  leaderboards; it never talks to osu!'s servers. Without a server
  configured, the game is fully offline.
- **Instruments and platforms:** tested mostly on ocarina, and on Linux.
  Windows and macOS builds are produced by CI and still need testing on
  real machines.
- **Levels to share:** songs bundled with the game must be free to
  distribute (public domain, freely licensed, or original).
- **BASS** (the audio library of the osu!lazer base) is free only for
  non-commercial use: see [docs/bass-licensing.md](docs/bass-licensing.md)
  before taking donations.

Open work in detail: [TODO.md](TODO.md). Anyone may take any of it: see
"Join in" in the [repository README](../README.md#join-in).

## Projects

| Folder | Assembly | What it is | Depends on |
|---|---|---|---|
| `core/` | `Kan.Core` | What a level is and how a play is judged: the score and design models, the live judge, timing, tracks and their edits, transcription drafts, the bridge to libkan. No osu! inside. | libkan (native) |
| `forge/` | `Kan.Forge` | **kan!forge**, the level editor's own tabs (tracks, compose, design), the audio played along a level, the media pipeline (separation, transcription, rendering). | `Kan.Core`, the fork |
| `ruleset/` | `osu.Game.Rulesets.Kan` | The **kan!** game mode: hit objects, the live play (listening, judging, the staff), mods, scoring, replays. | `Kan.Core`, `Kan.Forge`, the fork |
| `client/` | | The fork of osu!lazer (git submodule). Every change to it is listed in [docs/fork-divergence.md](docs/fork-divergence.md) and kept minimal: most are hooks for the ruleset to add its screens. | |
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

- [docs/gameplay.md](docs/gameplay.md): how a play works and is judged (at
  a glance first, then each rule with the reason behind it).
- [docs/editor-pipeline.md](docs/editor-pipeline.md): the editor (tracks,
  separation, transcription, compose, design, timing repair).
- [docs/fork-divergence.md](docs/fork-divergence.md): every change made to
  the osu!lazer base.
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

`KAN_VERBOSE=1` brings back libkan's console lines (progress as text,
the separation's device and model, ggml's backend): the game shows its own
progress, so they stay quiet otherwise. Failures always print.

Test runs belong in an isolated instance (a headless compositor, a copy of
the game's data, volume 0, its own network namespace): a developer's own
game and microphone are never touched. A test instance never opens the
microphone: without `KAN_LIVE_INPUT` it listens to nothing.

## Ground rules

- **Calm by design**: practice made fun, never anxiety or compulsion as
  the hook; forgiving defaults, strictness only by choice.
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
