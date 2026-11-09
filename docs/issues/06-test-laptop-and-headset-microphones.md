# Play-test kan! on laptop and headset microphones

Labels: `instrument-testing`, `audio`, `help wanted`
Size: M

## Context

kan! only needs "a plain microphone"
([README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)),
but so far it has been tested on one setup. Built-in laptop mics, cheap
headset mics, and consumer USB mics differ where it counts: laptop mics
sit next to fan noise and apply their own AGC, headset mics sit close to
the mouth (plosives, breath blasts) and pick up key/mechanical noise, and
budget mics have limited bandwidth. The preprocessing stage (35 Hz
high-pass + adaptive noise gate calibrated to room silence — see
[libkan/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/libkan/TODO.md))
was built for exactly this, and its calibration flow needs real-world
validation before we can tell players "any microphone works".

This is a first-impression item in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
("other microphones (laptop, headset, …)").

## What to do

1. Build and run `apps/kan_cli` and `apps/kan_demo`, and the game
   (`make run` in `game/`).
2. Test at least **one built-in laptop mic** and **one headset mic** (a
   consumer USB mic is a welcome third):
   - run the room-silence noise-gate calibration (`--noise-cal <ms>`) and
     report whether it converges and how it feels;
   - free-tuner: does a soft note register? does typing, fan noise or
     breath false-trigger onsets?
   - play one easy level end to end on your instrument through each mic;
   - report the perceived input→feedback latency (there is a formal
     harness in `docs/dsp/latency-measurements.md`, but "feels immediate"
     or "feels late" is already useful).
3. Record device details (OS, mic model, connection) and the game's
   behaviour. File findings as separate issues.

## Acceptance criteria

- [ ] A built-in laptop mic tested: calibration flow + one full level
- [ ] A headset mic tested: calibration flow + one full level
- [ ] False-trigger behaviour (breath, typing, fan) documented for each
- [ ] Soft-note sensitivity (pp) documented for each
- [ ] Findings filed as separate issues with device details

## Pointers

- `apps/kan_cli` — `--noise-cal <ms>`, `--no-gate`, `--profile …`,
  `--latency-report`
- `libkan` `precondition.hpp` — 35 Hz high-pass + adaptive noise gate
- [docs/dsp/latency-measurements.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/latency-measurements.md) —
  how latency is measured
- [docs/dsp/calibration.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/calibration.md) —
  what calibration measures
- `apps/kan_cli --loopback <wav>` — inject recordings to compare devices
  offline

## Notes

- If a device cannot work at all (e.g. hard AGC that flattens the
  signal), documenting the boundary is a valuable result — kan! should
  say so honestly rather than pretend.
- Related issue: the ambient-noise false-trigger gate has its own
  acceptance numbers and is tracked separately in `docs/issues/`.
