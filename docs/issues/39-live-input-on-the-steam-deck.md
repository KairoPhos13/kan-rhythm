# Live input on the Steam Deck: early placement, then silence

Labels: `game`, `libkan`, `timing`, `steam-deck`
Size: M

## Context

Owner QA on a Steam Deck (2026-11-14, v0.1.2 AppImage), playing an
ocarina into the microphone. The same levels play as expected on the
owner's desktop.

1. **The pitch trail is drawn ahead of the now line, and notes are judged
   early.** The dotted line of the note being played starts before the
   line that marks when to play. Restarting the level (without going back
   to the main menu) fixes it.
2. **Recognition stops all of a sudden.** In the middle of a play the
   game stops recognising notes and shows no played note, as if the
   microphone had been switched off. No "The microphone stopped for a
   moment" message was reported.

## Likely causes (from the code, to confirm with the Deck's logs)

**1, early placement.** A heard moment is placed by its sample count
since `LiveSession.Begin`, read as real time since that same moment
(`ListeningTimeline`, audit N11 in issue 38). If the microphone starts
delivering samples some time after `Begin` (a cold audio device on the
first play; a warm one on a restart), every sample is younger than its
count says and every sound lands early by that start-up delay. The same
happens after any gap shorter than the 1 s stall check. The fix is to
place samples by when they arrived (the lower envelope of arrival time
minus stream time, kept current), not by the count alone. Calibration's
microphone delay must be measured the same way.

**2, silence.** The microphone kept giving samples (the stall check
would otherwise reopen it and say so), so either the describer stopped
reporting (a state it does not leave, such as a NaN or a raised noise
floor) or the describer thread fell behind real time on the Deck's CPU
and its facts arrive too late to show. The play log tells these apart:
`kan!: play frames:` gives the describer's worst block and backlog, and
the play's `kan-play-*.csv` trace shows when readings stop and what was
heard last.

## What to do

1. Read the Deck's `runtime.log` and `kan-play-*.csv` files from plays
   that showed each problem.
2. Early placement: arrival-time placement in `LiveSession` and
   `ListeningTimeline`, with an offline test (repo rule 5): a source that
   starts late, and one with a gap, place a known onset within 10 ms.
3. Silence: fix what the traces show. If the describer falls behind,
   measure its cost per block on the Deck and cut it; if it gets stuck,
   reproduce with the recorded take and add the case to libkan's tests.

## Acceptance

- [ ] On the Steam Deck, the first play after starting the game places
      the trail on the now line, as a restart does.
- [ ] Ten minutes of play on the Deck without recognition stopping.
- [ ] Offline tests for a late-starting source and a gap.
