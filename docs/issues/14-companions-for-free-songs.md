# Companions for the free songs (artists: melodies + accompaniments)

Labels: `art`, `help wanted`
Size: L

## Context

Levels need songs that can be shared freely — the release checklist in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
says bundled songs must be "public domain, Creative Commons or
original", and the root
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
invites artists directly: *"If you write, play or arrange music —
melodies and their accompaniments (companions) — and would release some
of it under a free licence for kan!, you are very welcome."*

In kan!'s vocabulary the track that plays along with the player is the
**companion** (the editor's "accompaniment" track role — see
[game/docs/editor-pipeline.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/editor-pipeline.md)).
A melody without a companion is lonely to practise; a good companion is
what makes practice fun. Candidate songs are being collected in
`charts/docs/free-song-candidates.md` (that document is landing in the
charts/ lane — if the link 404s, check back shortly).

## What to do

1. Read `charts/docs/free-song-candidates.md`
   ([link](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/docs/free-song-candidates.md))
   for songs that are free to distribute and looking for companions.
   Original compositions are equally welcome — then you are both the
   melody and the companion.
2. For one or more songs:
   - write/arrange the **companion** part (any instrumentation: guitar,
     strings, keys, ensemble — say what it is);
   - record it (acoustic or sequenced — say which) as a steady-tempo
     audio file aligned to the melody (WAV/OGG preferred, MP3 fine);
   - also welcome: the melody itself as a clean reference recording
     (this becomes the level's reference track for transcription).
3. Release it under a free licence: **CC0, CC BY or public domain**
   preferred (avoid NC/ND so levels stay remixable and shippable). Send
   it as a pull request to `charts/` (or contact the maintainer to
   arrange the file transfer) with: song title, composer/arranger,
   licence, instrumentation, tempo, and which candidate it matches.
4. Credit goes on the tin: level cover credits are a first-class field
   ([game/docs/editor-pipeline.md §6b.1](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/editor-pipeline.md)).

## Acceptance criteria

- [ ] At least one companion recording (or a complete melody+companion
      original) delivered with licence and credits
- [ ] Steady tempo documented (BPM), aligned to the melody's timing
- [ ] Licence is free to distribute (CC0/CC BY/public domain recorded in
      writing)
- [ ] The song's entry in `charts/docs/free-song-candidates.md` (or
      `charts/official/`) updated to reflect it
- [ ] Credits text prepared for the level's cover field

## Pointers

- `charts/docs/free-song-candidates.md` — candidate songs (landing in
  the charts/ lane)
- [game/docs/editor-pipeline.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/editor-pipeline.md) —
  track roles (reference / accompaniment / demux), cover credits §6b.1
- [charts/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/README.md) —
  where song data lives; `charts/official/` for bundled content
- [assets/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/assets/README.md) —
  licensing ground rules for contributed media

## Notes

- In kan! the accompaniment is called the **companion** — please use
  that word in metadata and discussion.
- Calm by design applies to content too: gentle, playable arrangements
  are the heart of the library; intense songs are fine when the music
  wants it — the game itself never turns the pressure up.
