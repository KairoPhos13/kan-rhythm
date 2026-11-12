# tools/level_pack: build kan! starter-pack levels

A level of the starter pack is made from two things: a **score source** (a
MIDI with a melody track and the companion tracks that play along) and a
small **song.json** (the level's metadata, credits and licences, which track
is the melody, how far to transpose it, the tempo for calm play). This
folder turns them into an importable kan! level: an osu! `.osz` carrying the
melody as the score, the companion as the level's audio, the reference
(melody and companion together), a calm design and the level's credits.

The five levels live in `charts/official/<song>/`, one folder per song; this
tool is how they are built and rebuilt.

## What a level holds

| File | What it is |
|---|---|
| `<Artist> - <Title> (KairoPhos) [Calm].osu` | the osu! side: metadata, the tempo grid, the background, and the score's hit-object marks (one per note, one at the level's end, so the level's length reads right: [level-metadata](../../docs/architecture/level-metadata.md)) |
| `audio.ogg` | the **companion**: the accompaniment that plays along during the level, levelled to -20 LUFS |
| `reference.ogg` | the **reference**: melody and companion together, the performance the score came from |
| `kan.score.json` | the **score**: every note's timing, pitch, duration and intensity (the file the ruleset judges) |
| `kan.design.json` | the **design**: the calm look, section by section |
| `kan.credits.json` | the **credits** who made what the level is built on |
| `CREDITS.md` | the same credits for a human, with every source and licence |
| `background.png` | a calm gradient (original, CC0) |

## Where the music starts

Every level opens with one bar of soft, high count-in pulses (the first
beat a little louder), so a player hears the tempo before the first note.
Two `song.json` fields shape this:

- `skip_beats`: beats cut from the source's start, for a prelude without the
  melody (Amazing Grace's organ setting plays a whole verse before the tune).
  Cut so the melody's pickup still falls where it belongs in the bar.
- `count_in_beats`: the count-in's length in beats; one bar by default, `0`
  for none.

## Usage

```bash
# render one song's audio and kan! files into charts/official/<song>/level/
python3 tools/level_pack/make_level.py render charts/official/sakura-sakura

# rewrite only the level's .osu (its hit-object marks); the audio stays as it is
python3 tools/level_pack/make_level.py osu charts/official/sakura-sakura

# pack that level into charts/official/dist/sakura-sakura.osz
python3 tools/level_pack/make_level.py pack charts/official/sakura-sakura

# every song under charts/official/: render, then pack
python3 tools/level_pack/make_level.py build --all

# turn a reviewed text transcription into the MIDI the generator reads
python3 tools/level_pack/text_to_midi.py charts/official/kojo-no-tsuki/sources/kojo-no-tsuki.txt

# the generator's own tests
python3 -m unittest discover -s tools/level_pack/tests
```

**`pack` (and the CI build) needs only Python's standard library.** `render`
needs `numpy` plus an Ogg Vorbis encoder (`ffmpeg` on the path, else
`soundfile`). The rendered audio is committed under `charts/official/<song>/level/`,
so packaging a release never runs a synth: the zips are built from what is
reviewed in the repository, on any platform, in seconds.

## The score source

`sources/<slug>.txt` is the transcription itself, one line per note, so it
can be reviewed against the score it came from:

```
tempo 88
meter 4 4
melody            # what the player plays
  0 A4 1          # <beat> <pitch> <duration in beats>
  1 A4 1
  2 B4 2
companion         # what plays along
  0 [A2,E3,A3] 4  # a chord for four beats
```

`text_to_midi.py` builds that into a MIDI; `make_level.py render` reads a
MIDI like any other (including one downloaded from a public-domain source,
which is what `charts/official/amazing-grace` does). Where a melody is
transcribed by hand from a printed score, the transcription is our own CC0
work and `CREDITS.md` says so.

## Rendering: why a small synth

The route is route (b) of the song survey (`charts/docs/free-song-candidates.md`):
kan! renders its own companion from a public-domain score, so the audio is
as free as the score. A real synthesiser would be the first choice - the
plan was `nix run nixpkgs#fluidsynth` with a permissively licensed General
MIDI sound font - but this environment has no root (`/nix/store` is not
writable), no fluidsynth or timidity installed, and no sound font whose
licence was checked, so the generator carries its own gentle additive synth
(`kanpack/synth.py`): a fundamental with two quiet harmonics, a short attack
and release, the companion decaying softly like a plucked string and the
melody holding like a flute. The render is ours outright (CC0), which keeps
the pack's licensing as simple as the melody's. If a sound font and
fluidsynth become available, `kanpack/synth.py` is the one place to swap in
the real synth.

## Loudness

Every level's audio is levelled to the game's own reference: **-20 LUFS**
(ITU-R BS.1770-4) with the sample peak held under **-1 dBFS**, exactly what
the editor's render does (`game/core/Tracks/LevelLoudness.cs`). The
measurement in `kanpack/loudness.py` mirrors `libkan/include/libkan/loudness.hpp`
(K-weighting shelf and high-pass from the BS.1770 prototypes, 400 ms blocks
overlapping by 75 %, -70 LUFS absolute gate, then the -10 LU relative gate),
so what the pack measures is what the game measures. Moving between starter
levels never needs the volume touched; the offline tests hold the
measurement to known values (a full-scale 1 kHz sine reads -3.05 LUFS,
halving a signal is 6.02 dB).

## How the levels reach a player

The release zip carries the pack in `levels/` next to the game (built by
`game/packaging/windows/assemble.sh`), and the ruleset imports it the first
time the game runs - no account, no setup (`game/ruleset/Beatmaps/KanStarterPack.cs`).
The pack's `pack.json` holds a version; it is remembered in the ruleset's
settings, so each level is imported once per pack version and never again.

## Layout

```
tools/level_pack/
  make_level.py        render | pack | build (the CLI)
  text_to_midi.py      sources/<slug>.txt -> sources/<slug>.mid
  kanpack/
    smf.py             a small Standard MIDI File reader and writer
    level.py           MIDI + song.json -> score, design, credits, .osu, background
    synth.py           the gentle synth (companion and reference renders)
    loudness.py        ITU-R BS.1770-4, the game's own measurement
  tests/               offline tests (known input, expected output)
```
