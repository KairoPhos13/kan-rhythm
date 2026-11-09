# Level creator handbook: how to make a kan! level

Labels: `docs`, `editor`, `help wanted`
Size: L

## Context

kan! is "by the community, for the community"
([README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md)),
and the community's raw material is levels. The editor (kan!forge) is
powerful and already working — track roles, local stem separation,
transcription drafts, compose/design/verify tabs — but its only
documentation is an **implementation note** for developers
([game/docs/editor-pipeline.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/editor-pipeline.md))
and the player-oriented summary in
[game/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md).
A creator who wants to turn a song into a level today has no how-to.

The README's "Join in" section promises that artists and players can
contribute; a handbook is what makes that true for level creators. It
also matters for the release: `docs/issues/15` (first bundled level)
and `docs/issues/14` (companions) both need a document to point at.

## What to do

1. Write a creator-facing handbook (suggest `game/docs/level-creation-guide.md`,
   linked from `game/README.md` and the root README's "Join in") in
   plain language, aimed at musicians, not programmers:
   - **What a level is** — a song's score: reference track, companion
     (the accompaniment that plays along), the score, per-instrument
     arrangements, the look;
   - **Getting a song in** — local files only (URL/YouTube fetching is
     developer tooling, see the pipeline doc §7); licences: what may be
     used and why (link `charts/docs/free-song-candidates.md` for
     freely shareable material);
   - **Tracks tab** — roles (reference / accompaniment / demux), what
     separation does and does not do, the "never demuxable again" rule,
     cutting/moving ranges, cover credits;
   - **Compose tab** — the score editor: transcription drafts as a
     starting point (trust, then fix), pitch/onset/duration editing,
     tempo and sync points, the "unjudged" semantics;
   - **Design tab** — the level's look (calm and readable beats busy);
   - **Verify tab** — the checklist explained: unreachable notes, two
     notes at once on one-note instruments, guessed pitches, audio
     length/loudness; what each warning means and how to fix it;
   - **Sharing** — file formats today (`.kan`/`.kchart` direction:
     [charts/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/charts/README.md)),
     and the golden rule: every level must play on more than one
     instrument family where the music allows.
2. Include one worked example end to end (a small freely licensed song:
   import → separate → draft → correct → design → verify), with
   screenshots from the current editor.
3. Keep the project's vocabulary: **companion** for the accompaniment,
   **level** for the playable unit, kan!forge for the editor. Calm tone
   throughout.

## Acceptance criteria

- [ ] Handbook exists at a stable path and is linked from `game/README.md`
      and the root README's "Join in"
- [ ] Covers the full path: song in → play-ready level out, one section
      per editor tab
- [ ] One worked example with screenshots from the real editor
- [ ] Licensing guidance for source songs included (with the
      free-song-candidates pointer)
- [ ] Vocabulary consistent (companion, level, kan!forge) and the tone
      calm; reviewed by at least one non-developer reader if possible

## Pointers

- [game/docs/editor-pipeline.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/editor-pipeline.md) —
  the source of truth (§6a glossary, §6b tracks, §6f compose, §6g
  design, §6h verify, §7 URL policy)
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  how plays are judged (write charts that judge fairly)
- [game/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md) —
  the player-facing feature summary to stay consistent with
- `charts/docs/free-song-candidates.md` — songs that are free to use

## Notes

- If parts of the editor change while you write, note the version/date
  in the doc header; a slightly stale handbook beats none.
- Creator docs are never finished: leave an "improvements welcome"
  pointer and file follow-ups as issues instead of blocking the first
  version.
