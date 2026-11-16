# A level keeps its stems; its audio travels compressed

Labels: `game`, `editor`, `server`, `level-format`
Size: L

## Context

Owner, 2026-11-16: levels should keep every separated track, so that
someone can later make the bass level, the guitar level, and so on from
the same song. Separating again from a mix loses quality each time. But
play needs a single mix, to keep the cost low.

Both are right, and they do not conflict: what a level **stores** and
what it **plays** can differ.

## What was measured (2026-11-16)

"No hay lugar más alto", 6 min 11 s, six stems from the editor's demux:

| Stems stored as | Size of the six |
|---|---|
| WAV, 32 bit float (as the demux writes them) | 750 MB |
| FLAC, 16 bit (lossless) | 96 MB |
| Opus 160 kbps | 49 MB |

The server takes at most 100 MiB per level package
(`SUBMISSION_MAX_PACKAGE_BYTES`).

**Found on the way: levels already outgrow the limit.** The level audio
is stored as a 16 bit WAV (about 65 MB for this song), and since the voice
file (`... - level voice.wav`, 2026-11-16) a level with a voice carries a
second WAV of the same length: about 130 MB, over the limit, before any
stem.

## Direction

1. **Play files compressed.** The level audio and the level voice are
   written as Ogg Vorbis (high quality, about 6 to 9 MB for this song
   each). BASS plays Vorbis on every platform, and libkan reads it once
   stb_vorbis (public domain) is compiled into miniaudio. Play cost does
   not change: still one mix, plus the voice when the level has one.
   This step alone brings this level from about 130 MB to under 20 MB.
2. **Stems stored, never played.** The demuxed stems go into the level
   under `stems/`, lossless (FLAC) when the package stays within the
   limit, else Ogg Vorbis at a transparent quality. Play never opens
   them. The level file says which stems it has and that they came from
   the same mix (same length, same start), so they line up.
3. **Making a level from another level.** The editor offers the stems of
   an existing level as tracks (decoded once to WAV in the work folder,
   as imports are today), so a bass level starts from the bass stem, not
   from a new demux of the mix. The new level credits the level it came
   from (creators are credited, owner rule).
4. **Downloads stay light.** A player who only plays does not need the
   stems. The server keeps the full package and serves the play package
   (without `stems/`) by default; the full one is for creators.
   Optional for the first version: if the server keeps one package,
   steps 1 to 3 still fit the limit with Vorbis stems.

## Open choices (owner)

Resolved 2026-11-16: the stems travel as **Ogg Vorbis** (FLAC was
weighed and dropped: no new library, smaller levels and downloads) and
as **one package** for now; the light play package without stems is the
server side's (work package 2).

## Dependencies

Encoding needs an encoder in the native bridge: libvorbis and libogg
(BSD) for Vorbis, libFLAC (BSD) for FLAC. miniaudio only writes WAV.
Both build on Linux, Windows (MinGW) and macOS; they go through
`kan_build_policy.cmake` like everything shipped.

## Progress

**Step 1 done (2026-11-16).** The owner chose Ogg Vorbis for the played
files ("si ogg es mejor vamos por esa ruta"); MP3 stays readable
everywhere (import, the game, libkan).

- The native bridge encodes Ogg Vorbis: `kan_forge_encode_ogg`, libogg
  1.3.6 and libvorbis 1.3.7 built from source into the bridge
  (`game/forge/native/cmake/kan_vorbis.cmake`), deterministic output (a
  fixed stream serial, so a file's hash changes only with its sound).
- The bridge also reads Ogg now (stb_vorbis from miniaudio's extras), so
  every native decode (imports, loudness, transcription) takes it.
- The editor writes "<title> - level audio.ogg" and "<title> - level
  voice.ogg" at Vorbis quality 0.6 (`BeatmapAudio.PLAY_FILE_QUALITY`). The
  renders stay WAV in the work folder; only what the level carries is
  compressed.
- The format is part of the level audio's signature: a level written as
  WAV renders again as Ogg on its next save in the editor, and the old WAV
  files are removed. Voice files of both formats are found.
- Measured on "No hay lugar más alto" (the level audio is mono 48 kHz,
  6 min 11 s): 35.7 MB as WAV, 4.3 MB as Ogg, encoded in 1.5 s.
- Tests: the bridge selftest (an Ogg file, smaller than a third of the
  WAV, the same bytes twice, decoded back at full length with more than
  30 dB SNR, the same transcribed notes) and `LevelPlayFileTest` (BASS
  plays the Ogg at its rate, channels and exact length).

Next: step 2 (stems stored), then levels from levels and the server
side. The owner's choices on FLAC or Vorbis stems and one or two
packages are still open.

**Step 2 done (2026-11-16).** The owner chose **Ogg Vorbis** for the
stems too ("stems are Ogg Vorbis, NOT FLAC"): no new library, smaller
levels and downloads, transparent at the quality used. One package for
now; the light play package without stems stays with the server side.

- A level keeps its stems under `stems/`: `stems/<kind>.ogg` for every
  stem of ONE demux (vocals, bass, drums, guitar, piano, other) and
  `stems/stems.json` (rate, channels, frames, the model and its grade,
  a sha256 per stem, and the signature of the source files, so the same
  stems are attached once). They are the raw separation: full song
  length, no gain, edits or trims. Stems from more than one separation,
  or of different lengths, are refused with a clear error.
- The stems are encoded at Ogg quality 0.8 (`LevelStems.STEM_QUALITY`),
  above the 0.6 of the played files: they are source material for the
  levels made from them later. They attach where the level audio is
  rendered and on the save after a demux, so a demuxed level carries its
  stems from its next save. A failure never fails the render.
- Play never opens them: the voice lookup, the level audio and voice
  naming, the resources tab's video lookup and the fork's verify checks
  all pass over `stems/` (fork F-34).
- The submission and the plain `.osz` export carry the stems. When the
  package would pass 95 MB (under the tunnel's 100 MB request body and
  the server's 100 MiB) the upload travels without them and the creator
  is told in plain words; the creator's own level keeps them either way
  (fork F-35).
- `LevelUpgrade.UpgradeAllAsync` turns the WAV level audio and the WAV
  voice of the local levels into Ogg (the same samples in, Ogg out),
  stores the level audio's signature in the set so the editor does not
  render it again, and attaches the stems from each level's work folder.
  A level with an imported song is left alone; a second run changes
  nothing. No UI yet: the architect adds the button.
- Found on the way and fixed: a kan! level with notes could not be
  exported or submitted at all (the legacy .osu encoder reads a hit
  object position, kan! claims legacy id 0, and `KanHitObject` had
  none). It now carries the centre of the osu! playfield.
- Measured on "No hay lugar más alto" (6:11, six stems, 44.1 kHz
  stereo, the owner's data, read only): the six stems are 750 MB as WAV
  float and 31.4 MB as Ogg at 0.8, encoded in 18 s. The round trip
  (Ogg back to PCM16) measures 26 to 38 dB SNR per stem: bass 37.7,
  piano 33.7, drums 32.0, vocals 29.3, guitar 28.0, other 25.9. All six
  keep their 16,379,904 frames. With the Ogg level audio and voice
  (about 9 MB) the level is about 41 MB with its stems: one upload, and
  room for songs up to about 14 minutes.
- Tests: selftest section 8b (a stereo round trip at 0.8 keeps its
  frame count and its channels apart, and is no further from the source
  than the played files at 0.6), `LevelStemsTest`,
  `PlayNeverOpensStemsTest`, `LevelStemsSubmissionTest`,
  `LevelUpgradeTest` and `LevelExportTest`, all on levels in a temporary
  realm.

Next: step 3 (a level made from another level's stems) and the server
side (a light play package without stems).

## Acceptance

- [x] The level audio and the voice are Ogg Vorbis; a level with a voice
      of six minutes is under 25 MB without stems.
- [x] Existing WAV levels keep playing; the next render writes Vorbis.
- [x] A demuxed level carries its stems under `stems/`, and the package
      stays within the server's limit for a song of 6 minutes (measured:
      about 41 MB with stems, one upload, the upload leaves the stems
      out above 95 MB).
- [x] Play opens no stem (measured: the same files open as before).
- [ ] The editor can start a new level from another level's stems,
      without a demux, and credits the source level.
- [ ] The stems of a level line up with each other and with the level
      audio to the sample (a test with known input). What step 2 checks:
      one separation per level, one frame count for all the stems, and
      the exact frame count kept through the encoder.
