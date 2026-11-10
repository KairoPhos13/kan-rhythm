# BASS replacement study: scope the clean-room "kanplay" option (D)

Labels: `licensing`, `docs`
Size: M

## Context

The kan! client's music/SFX playback runs on BASS (un4seen) through
osu!framework, proprietary binaries under a "free for non-commercial
use" licence. The analysis of record is
[game/docs/bass-licensing.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/bass-licensing.md):
it presents four options and recommends **option D** (a clean-room
drop-in replacement of the used BASS API surface (working name
"kanplay") built entirely from permissive parts (miniaudio decoders +
device output, Signalsmith via kanfx, our own mixer)) as the permanent
answer, deliberately deferred while the MVP came first. The decision
trigger is unchanged: before the first public download or first euro
received. The release is close, so the study needs to be done: not the
implementation yet, but the concrete engineering plan that makes the
decision easy.

The study is documentation and measurement work; the actual
implementation and any purchase/verification decision are the
maintainer's. Funding-model questions are out of scope here (a pending
maintainer decision).

## What to do

1. Turn §5 ("Option D in detail") of
   [game/docs/bass-licensing.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/bass-licensing.md)
   into an implementable plan in `docs/` (a new note linked from the
   BASS doc and `docs/TODO.md`):
   - re-verify the **used API surface** (~25-30 entry points of the
     ~136 in the ManagedBass binding): re-audit the exact
     `ppy.osu.Framework` version the fork builds against and list every
     entry point with its call sites (the 2026-10-12 audit is the
     starting point, sources listed in the BASS doc §7);
   - group the surface by function (playback control, position/time
     conversion, samples/SFX, device handling, file-procedure streaming,
     mixer subset, tempo/rate FX subset) and map each group to the
     permissive parts (miniaudio decoders, miniaudio/SDL output,
     Signalsmith Stretch, own mixer; all licences already cleared in
     the doc);
   - specify the shim strategy: forking the MIT `ManagedBass` binding
     and pointing its native-library constant at `libkanplay`, keeping
     osu!framework unchanged;
   - list the **clean-room rules** to follow (from published docs and
     black-box behaviour only; own declarations; no "BASS" trademark in
     the shipped product) and the pre-distribution IP-review checklist
     item;
   - produce the **latency re-validation plan** (the risk that matters
     technically): which measurements from
     `docs/dsp/latency-measurements.md` must be repeated on music
     playback and SFX, and what "no regression" means.
2. Estimate effort (per group, per platform) and identify the riskiest
   parts (device handling and file-procedure streaming are the usual
   suspects, confirm or refute with evidence).
3. Leave the decision itself to the maintainer: end the note with the
   options as they stand (D plan + cost, one-time licence cost if
   purchased, written verification route) and no recommendation beyond
   what the existing doc already says.

## Acceptance criteria

- [ ] The used BASS API surface re-audited against the fork's current
      `ppy.osu.Framework` version, with call sites listed
- [ ] Every surface group mapped to a permissive implementation part
      with its licence
- [ ] Shim strategy (ManagedBass fork → `libkanplay`) spelled out
- [ ] Clean-room rules and the pre-distribution review checklist
      included
- [ ] Latency/quality re-validation plan written
- [ ] Effort and risk estimates per group and platform
- [ ] The note linked from `game/docs/bass-licensing.md` and
      `docs/TODO.md`; no scope creep into implementation or funding
      decisions

## Pointers

- [game/docs/bass-licensing.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/bass-licensing.md):
  the analysis of record (§1 stack table, §5 options, §6 recommendation)
- [docs/architecture/osu-integration.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/architecture/osu-integration.md) §2/§4:
  scope boundaries (instrument capture is already ours, miniaudio)
- `kanfx/`: Signalsmith Stretch already used for the consonance shift
- [docs/dsp/latency-measurements.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/latency-measurements.md):
  measurement harness to extend for playback validation
- [docs/adr/0010-osu-lazer-base-client.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/adr/0010-osu-lazer-base-client.md) §5:
  why this is ship-blocking

## Notes

- Not legal advice, and none of this changes what ships: until the item
  closes, the release stays free and non-monetised (existing gate in the
  BASS doc §6).
- The clean-room rules in the doc are load-bearing: never decompile
  `bass.dll`, never copy `bass.h`, no "BASS" strings or trademarks in
  our binaries.
