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

## Progress

**2026-11-15, the pitch reading (done).** The live describer's 10 ms
pitch reading (YIN in `HybridDetector::detect_pitch_into`, also used
inside the note describer) built the difference function for every lag
up to 1024 and only then looked for the first lag under the threshold.
It now stops at that lag. Same operations in the same order: the output
is identical bit for bit (`libkan/tests/yin_early_exit_test.cpp`, 1725
windows against the full pass, 0 differences; the game's describer
gave byte-identical events on the five starter recordings and a
melody-only take). CPU time of the whole live describer, one core,
uncontended:

| Input | Before | After |
|---|---|---|
| Sakura Sakura, melody alone (what a player plays) | 15.2 % of real time | 3.1 % |
| The five starter references (melody and companion) | 16 to 17 % | 11 to 12.6 % |

**What the bench showed next.** On two cores the whole game used about
64 % of one core (update thread 34 %, draw 17.5 %, audio 6 %; the
describer under 1 %), so the stutter is not a lack of CPU: it is the
update thread taking 30 to 100 ms now and then. The cause is below.

**2026-11-15, the stutter in play (done).** A sampling profile of a
two-core play (`dotnet-trace` launched inside the isolated instance)
put `Kan.Forge.Play.LevelPreview.Update` on 68 % of the update thread's
samples, nearly all of it waiting: the song select preview lives on the
whole game and, away from song select, kept its stopped part cued to the
level audio every frame. With the level running that was a
`TrackBass.Seek` each frame, which the update thread waits on the audio
thread for. It is now stopped once on leaving song select and left
alone until song select is back (where starting it places it anyway).
Bench, Sakura Sakura on two cores, frames over 25 ms per play: 237, 428,
239 before; 100, 25, 17, 12, 19 after. What remains over 50 ms is at the
start of the level (loading, first-time compilation, a collection) and
at its end, not during play.

`game/tools/bench-play.sh` runs the bench; the play trace now has a
`load` row each second (the describer's worst block, and the frame
rates reached and aimed at).

**2026-11-15, the frame rate.** The game reaches its frame limiter's
2x refresh (draw 120, update 240 on a 60 Hz display) on two cores during
play. The bench had shown 60: a headless display never focuses the
window and an unfocused game drops to 60 Hz. `KAN_DEV_FOCUSED=1` (set by
the bench) keeps the focused rates for an unfocused developer run.

**2026-11-15, dense levels (done).** Piano levels made locally from
MAESTRO (issue 41), up to 15 keys held and ~16 notes a second, were the
stress test: in autoplay the Chopin Etude Op. 25 No. 10 excerpt ran at a
third of real time and failed. Four causes, none specific to an
instrument, so every level gains:

1. **A replay's facts waited while the clock caught up.** The play state
   returned early while osu!'s frame-stable clock caught up, but the
   notes were judged then: each closed as not heard before its facts
   arrived (autoplay "Replay failed", 47 %). The facts now reach the
   judge during the catch-up too.
2. **Note drawables alive 10 s ahead.** osu!'s default lifetime kept
   every note of the next 10 s alive and checked each frame; kan!'s
   note drawables draw nothing (the staff draws the notes). They live
   from 2.5 s before the note (the judge's earliest window is 1.12 s).
3. **Staff views changed note as the notes scrolled.** A note now keeps
   its view while in sight (index modulo the pool, depth by index), so
   its text is laid out once.
4. **The note name's font was set every frame**, and setting a font lays
   the text out again even when it is the same. It is set when it
   changes. This was the largest single cost.

Autoplay, same machine (busy with another workload), music seconds per
5 s of real time in the densest part: 0.8 to 1.2 before, 4.4 to 5 after;
Chopin 1048/1048 perfect (was failed at 6.8 s), Liszt "Wilde Jagd"
1051/1051, Nocturne Op. 9 No. 2 580/580 in real time. The Liszt's
densest bars still run at 60 to 80 % of real time on a loaded machine;
what remains is osu!'s scene graph spread thin, with no single cause.
Sakura on two cores: same verdicts and frame rates as before.

## Acceptance

- [ ] Bench on two cores: no stall over 50 ms in a full play; the
      describer under 3 ms per 10 ms at its worst.
- [ ] The judging tests give the same verdicts as before.
- [ ] The owner plays the five starter levels on the Deck without
      noticing a stutter.
