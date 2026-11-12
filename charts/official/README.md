# charts/official: the kan! starter pack

Five levels a player finds the moment they install kan!, free to distribute
and free to play: no account, no making one, nothing to download (issue 15).
They are built from public-domain scores and MIDI files by
[`tools/level_pack`](../../tools/level_pack/README.md), route (b) of the song
survey in [`charts/docs/free-song-candidates.md`](../docs/free-song-candidates.md):
kan! renders its own companion from a public-domain score, so the
composition and the audio are both free and the score is exact.

| Level | Origin | Source of the melody | Difficulty |
|---|---|---|---|
| `sakura-sakura` | Japan, traditional school song | the CC BY 2.5 score by Sakurambo | 1 |
| `kojo-no-tsuki` | Japan, 1901, Rentaro Taki | the CC0 score typeset by Cdang | 3 |
| `mo-li-hua` | China, traditional (Barrow, 1804) | the public-domain score notated by Asoer | 2 |
| `xicochi` | Mexico, Gaspar Fernandez, c. 1601 | **our own setting** (the facsimile is not legible) | 1 |
| `amazing-grace` | hymn, tune NEW BRITAIN (1829) | the CC0 organ setting by Rabanus Flavus | 2 |

Two Japanese, one Chinese, one Latin American, one hymn; slow and simple
enough for a first play, on a 12-hole alto C ocarina (A4-F6, concert pitch).
Every level is levelled to the game's loudness reference (-20 LUFS) and
plays a gentle **companion** under the player, with a **reference**
render (melody and companion together) alongside.

## What is in a song folder

```
charts/official/<song>/
  song.json        the level's metadata, licences, credits and build settings
  CREDITS.md       the same credits for a human (generated, kept with the level)
  sources/         what the level was made from: the score or MIDI, and the
                   reviewed transcription (sources/<slug>.txt) where we typeset one
  level/           the generated level files (committed, reviewable): the .osu,
                   kan.score.json, kan.design.json, kan.credits.json, the audio
```

`charts/official/dist/` holds the built `.osz` files and the pack manifest;
it is generated (`make_level.py pack --all`) and never committed.

## Licensing

Both the composition and the audio must be free to distribute: public
domain, CC0, CC BY or CC BY-SA (no NC/ND). Every level's `CREDITS.md` names
each source, its URL and its exact licence, and says what was derived from
what. Melodies transcribed by hand from a printed public-domain score are
our own CC0 work; the rendered audio and the level files are KairoPhos,
CC0. `xicochi` is labelled honestly: its melody is our own setting of the
villancico, not a transcription (the seventeenth-century facsimile is not
legible enough to read), with the composition credit where it belongs.

When the pack changes, bump `pack.json`'s `version`: the game imports the
levels once per pack version.
