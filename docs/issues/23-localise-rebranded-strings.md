# Localise the rebranded strings: other languages still say "osu!"

Labels: `localisation`, `help wanted`, `good first issue`
Size: M

## Context

The English string sweep renamed the game to kan! (first pass,
2026-10-27), but **translations were not touched**: "other languages
come from `osu.Game.Resources`' translations and still say osu!", see
"String sweep: remaining scope" in
[game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)
and the release item in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md).
A player running kan! in French, German, Japanese or Spanish currently
sees the old game's name in their own language's strings.

kan! is community-driven and wants players everywhere ("your church,
your friends, your community", see
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md));
this is exactly the kind of task fluent speakers can do better than any
maintainer. Per
[CONTRIBUTING.md §1](https://github.com/KairoPhos13/kan-rhythm/blob/main/CONTRIBUTING.md),
source-of-truth strings stay English; this issue is about the
translated values.

## What to do

1. Inventory: find the translated strings whose text says "osu!" where it
   means **the game itself** (the strings renamed in the English pass:
   first-run setup, settings, folders, fullscreen, exit dialog, update
   and audio notifications; the `kan! rebrand (ADR-0011)` markers in
   `Localisation/*Strings.cs` show which ones).
2. For each language you speak:
   - update the translated value to kan!'s name in that sentence, with
     the same meaning and tone as the new English text;
   - where a translation is now stale in meaning (not just the name),
     either fix it or **remove the translation** so the string falls
     back to the updated English (an honest English string beats a
     wrong translated one);
   - keep "osu!" where the string refers to real osu! (e.g. osu!stable
     import copy; that feature is scoped in `docs/issues/24`) and keep
     all attribution intact.
3. Send one PR per language (or per language group) naming the language
   and listing the strings touched. If you speak a language not yet
   offered by the client, say so; the list of supported locales is
   part of the deliverable.
4. Record the state: which languages are clean, which still say osu!,
   in a short table in `game/docs/` or in the tracking issue.

## Acceptance criteria

- [ ] For each language handled: no translated string says "osu!" where
      it means kan! itself
- [ ] Stale translations either corrected or removed (falling back to
      English), never left contradicting the English
- [ ] A status table: language → clean / partial / not started
- [ ] Each PR names the language and the strings touched
- [ ] Attribution and genuine-osu! references left intact

## Pointers

- [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md):
  "String sweep: remaining scope" (translations bullet)
- `osu.Game/Localisation/*Strings.cs` (fork): the rebranded English
  strings and their markers
- `osu.Game.Resources` translations: where translated values live
- [CONTRIBUTING.md §1](https://github.com/KairoPhos13/kan-rhythm/blob/main/CONTRIBUTING.md):
  English source-of-truth rule (does not block fixing translations)

## Notes

- Good first issue for bilingual contributors: no build-system
  knowledge needed, just careful language work.
- Tone matters in translation too: kan! is calm and forgiving; avoid
  urgent or punishing phrasing even if the old osu! translation had it.
