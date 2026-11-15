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

## What to do

1. **A repeatable bench.** A script that plays a starter level from its
   reference recording on two cores and prints the hitch count, the worst
   stall and the describer's cost. Every change below is measured with it.
2. **The update thread.** Profile a two-core play (`dotnet-trace`) and fix
   what costs: per-frame allocations and LINQ in the staff, the judge and
   the scene; work redone every frame that changes only when a note does.
3. **The describer for one-note instruments.** A profile that plays one
   note at a time needs no chord analysis; it can also analyse at a lower
   sample rate (16 kHz covers an ocarina and its overtones) and compute
   pitch only as often as the screen needs it. Offline tests keep the
   judging identical (repo rule 5).
4. **Defaults for handhelds.** A frame limiter at the screen's refresh
   (not twice it) when the game detects a Steam Deck, or a "battery saver"
   switch in Settings > kan!.

## Acceptance

- [ ] Bench on two cores: no stall over 50 ms in a full play; the
      describer under 3 ms per 10 ms at its worst.
- [ ] The judging tests give the same verdicts as before.
- [ ] The owner plays the five starter levels on the Deck without
      noticing a stutter.
