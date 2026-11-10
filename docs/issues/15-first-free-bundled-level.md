# Ship a first free bundled level (charts/official/)

Labels: `editor`, `art`, `help wanted`
Size: L

## Context

A new player's first minutes decide whether kan! is fun. Today the game
ships with no levels: "bundled songs free to distribute (public domain,
Creative Commons or original): none popular" is an open release item in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md),
and [charts/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/TODO.md)
wants "1 demo song fully authored … with guitar + flute + piano
arrangements, exercising all 3 layers". The editor (kan!forge) can
already take a song from audio to a playable level: track roles,
separation, transcription drafts, compose/design/verify tabs (see
[game/docs/editor-pipeline.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/editor-pipeline.md)).

Candidate free songs are being collected in
`charts/docs/free-song-candidates.md`
([link](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/docs/free-song-candidates.md);
landing in the charts/ lane; if it 404s, check back shortly).

## What to do

1. Pick a song that is free to distribute (from
   `charts/docs/free-song-candidates.md` or an original of your own;
   licence must allow bundling: CC0/CC BY/public domain preferred).
2. Author the level in the in-game editor (kan!forge):
   - **tracks:** import the audio, choose the **companion** (the
     accompaniment that plays along), optionally separate stems
     locally;
   - **compose:** write or correct the score (transcription drafts are a
     starting point, never gospel);
   - **design:** the look while playing (keep it calm and readable);
   - **verify:** run the checklist (reachability, two-notes-at-once on
     one-note instruments, guessed pitches, audio length/loudness) and
     fix what it flags.
3. Author arrangements for at least **two instrument families** (e.g. a
   wind and a plucked/keyboard one); the whole point is that any
   instrument kan!s. Keep each arrangement under the size budget
   (< 50 KB per `.kchart`, see
   [charts/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/TODO.md)).
4. Land it under `charts/official/` as a pull request (charts are data:
   text-friendly, diff-reviewable), with metadata in English, licence
   and credits recorded.

## Acceptance criteria

- [ ] One complete level in `charts/official/`: audio, score, and at
      least two per-instrument arrangements
- [ ] The verify-tab checklist passes with nothing ignored
- [ ] Licence documented (free to distribute) with credits for the song
      and each arrangement
- [ ] The level plays end to end on at least two different instrument
      families
- [ ] Level audio rendered at the reference loudness (−20 LUFS; the
      editor's render handles this, verify it)
- [ ] Entry added to `charts/docs/free-song-candidates.md` or
      `charts/official/` index as appropriate

## Pointers

- [game/docs/editor-pipeline.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/editor-pipeline.md):
  the whole pipeline (§6b tracks, §6f compose, §6h verify)
- [game/docs/gameplay.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/gameplay.md):
  how levels are judged (write charts that judge fairly)
- [charts/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/README.md),
  [charts/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/charts/TODO.md):
  layout, "charts are data" rule, size budgets
- `charts/docs/free-song-candidates.md`: candidate songs

## Notes

- The accompaniment is the **companion** in kan!'s vocabulary.
- Arrangements must be generalist-friendly: note the instrument and
  difficulty tier in the chart metadata rather than assuming one player.
- A level that is easy, clear and fun beats one that is impressive;
  forgiving defaults are the project's promise.
