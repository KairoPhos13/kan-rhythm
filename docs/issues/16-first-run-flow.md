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

- [x] A fresh install goes welcome → instrument → mic test → (calibrate)
      → easy level without leaving the flow
- [x] The instrument choice persists and appears in the existing
      settings selector
- [x] The mic test shows live level + note-detection feedback and has
      clear, kind failure states (no mic, permission denied, silence)
- [x] Every step can be skipped and re-run later
- [x] No step assumes a specific instrument family
- [ ] Screenshots of the whole flow included in the PR

## Implementation (2026-11-12)

The flow is built in the ruleset, added to the game the way
`KanStarterPack` and `KanDevPlay` are (`KanRulesetIcon` in
`game/ruleset/Beatmaps/KanLevelFiles.cs`): no change to the fork.

- `game/ruleset/FirstRun/KanFirstRun.cs`: the component that shows the
  guide once on a fresh install (after osu!'s own first-run setup, at the
  main menu) and opens it again from Settings; closing it at any point is
  skipping it, and nothing asks again (`KanRulesetSetting.FirstRunDone`).
- `game/ruleset/FirstRun/KanFirstRunOverlay.cs` + `KanFirstRunScreens.cs`
  + `KanFirstRunMicrophone.cs`: the five steps (welcome, your instrument,
  your microphone, calibration, your first level) in kan!'s own wizard
  shell over the fork's `WizardScreen` steps (the fork's shell lives on
  the game's screen footer, which a ruleset cannot reach).
- The mic step listens with the game's own pipeline
  (`KanLiveListener` → `LiveSession` → libkan's describer): a live level
  meter, "there it is: I heard C5" on a note, and kind failure states
  ("I cannot hear anything yet: try X"). The microphone can be chosen
  (`MicSource.RecordingDevices`).
- The level step lands on the shortest starter level (Xicochi, issue 15's
  pack) and starts it in the player; with no level it says so honestly.
- Re-run from Settings → Rulesets → kan! ("Run the first-run guide
  again…"); every choice it takes is one of the existing settings
  (`KanPlayer`, the instrument wedge's choices).

Verified: `make check` green (0 warnings; native self-test, Kan.Core,
Kan.Forge suites), and the flow's code paths by reading. Not verified
here: a running client (this build machine never launches the game), so
the screenshot criterion stays open. The dev hooks walk the flow in the
isolated instance: `KAN_DEV_SCREEN=firstrun` (with
`KAN_DEV_STEP=welcome|instrument|microphone|calibration|level`) and
`KAN_DEV_SCREENSHOTS=…` for the screenshots.

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
