# First run: choose the instrument, test the microphone, play an easy level

Labels: `game`, `help wanted`
Size: L

## Context

The release checklist in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
wants the first run to be one gentle path: *"choose the instrument, test
the microphone, calibrate, play an easy level"*. Today the pieces exist
but are scattered: instrument selection lives in Settings → Rulesets →
kan! (a category→instrument dropdown, `KanInstrumentCatalog`, persisted
across runs; fork-era plugin surface, zero divergence), and there is no
guided microphone test or landing level.

First impressions set the tone for the whole product. kan! is "calm by
design, forgiving by default"
([game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)):
the first run must never scold, rush, or dead-end. Everything must be
skippable and re-runnable later from settings.

## What to do

1. Design and implement a first-run flow (the fork already has
   first-run setup copy that the string sweep touched, see
   `Localisation/*Strings.cs` deltas and
   [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)):
   - **Welcome**: one screen, what kan! is ("grab your instrument"),
     no pressure, a Skip;
   - **Instrument**: pick category + instrument (reuse
     `KanInstrumentCatalog` and the v0 selector; remember the choice);
   - **Microphone test**: pick the input device, show a live level
     meter plus "play a note" detection feedback (the same pipeline the
     game uses; loud/quiet/triggered states shown kindly, e.g. "I can
     hear you" / "I cannot hear anything yet: try X"), then the
     calibration entry (tracked separately in `docs/issues/17`;
     integrate, do not duplicate);
   - **First level**: land the player on an easy level (whatever
     bundled/own level exists; degrade gracefully when there is none).
2. Make every step skippable, every choice changeable later in settings,
   and every failure non-blocking (a dead mic should not trap the player:
   offer "play later" honestly).
3. Keep it general: no step may assume one instrument family; winds,
   strings, keys and voice all pass through here.

## Acceptance criteria

- [ ] A fresh install goes welcome → instrument → mic test → (calibrate)
      → easy level without leaving the flow
- [ ] The instrument choice persists and appears in the existing
      settings selector
- [ ] The mic test shows live level + note-detection feedback and has
      clear, kind failure states (no mic, permission denied, silence)
- [ ] Every step can be skipped and re-run later
- [ ] No step assumes a specific instrument family
- [ ] Screenshots of the whole flow included in the PR

## Pointers

- [game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md):
  "First run" release item; instrument selection v0 notes
- [game/docs/instrument-modes.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/instrument-modes.md):
  instrument taxonomy and the v0 selector
- `KanInstrumentCatalog` (ruleset config persistence, `client.realm`)
- `apps/kan_cli` / `apps/kan_demo`: the live meter/detection feedback to
  mirror
- [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md):
  first-run setup strings were already adapted; keep fork deltas minimal
  and logged

## Notes

- Copy tone matters as much as flow: calm, concrete, never scolding.
  "Forgiving defaults" includes the words on the screen.
- A new player should be playing within a few minutes, with no account,
  no network, and nothing recorded or sent
  ([game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)).
