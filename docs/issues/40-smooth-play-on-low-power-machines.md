# Smooth play on low-power machines (Steam Deck)

Labels: `game`, `libkan`, `performance`
Size: M

## Context

Owner QA on a Steam Deck (2026-11-14): with a microphone that works, the
game still stutters now and then. An ocarina plays one note at a time,
the lightest case kan! has, so it should run without strain on a
handheld.

Measured the same day in the isolated instance, Sakura Sakura played from
its reference recording in real time (`KAN_LIVE_INPUT_REALTIME=1`), the
hitch monitor's log lines:

| | Hitches in ~60 s | Update thread stall | Describer, worst |
|---|---|---|---|
| All 12 cores | 4 | 33 to 114 ms | 1.9 ms per 10 ms of audio |
| 2 cores (`taskset -c 0,1`) | 29 | mean 50 ms, worst 299 ms | 19.5 ms per 10 ms of audio |

Every hitch is on the update thread (draw 0 ms, GC 0 to 10 ms). On two
cores the describer's worst block took longer than the audio it
described, so it falls behind for a moment. Two cores of this desktop are
roughly what the Deck gives a game while it also runs the GPU at a low
power limit.

## Rules (owner, 2026-11-15)

- **Never remove a capability.** No lighter mode, no lower sample rate,
  no skipped analysis, no feature switched off for weaker machines. The
  game does everything it does today; it only does it with less work.
- **Only clear improvements.** Every change is measured before and after
  on the same bench, gives the same judging, and makes no measure worse.
- **Moderate priority.** In kan! the player follows the music, not the
  screen, so a late frame matters less than in osu!. The stutter seen on
  the Deck on 2026-11-14 may have come largely from PipeWire's processing
  of the default microphone (issue 39), which also runs on the Deck's CPU;
  with the raw input the owner saw no stutter on 2026-11-15.

## What to do

1. **A repeatable bench** on two cores (hitch count, worst stall, the
   describer's cost per block), run before and after every change.
2. **The update thread.** Profile a two-core play and remove waste only:
   per-frame allocations, LINQ in hot paths, work redone every frame
   that changes only when a note does. Same output, less work.
3. **The describer.** The same analysis, done with less work (no
   repeated computation, no allocation per block), with offline tests
   that show identical results (repo rule 5).

## Acceptance

- [ ] Bench on two cores: no stall over 50 ms in a full play; the
      describer under 3 ms per 10 ms at its worst.
- [ ] The judging tests give the same verdicts as before.
- [ ] The owner plays the five starter levels on the Deck without
      noticing a stutter.
