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

3. **Calibration measures 460 ms of microphone delay** (8 clicks, 7
   heard, spread ±79 ms). The step measures the whole round trip: the
   click's way out (game audio buffer, PipeWire, the speaker or
   headphones), the microphone's way in, and recognition. On a desktop it
   is tens of ms. Suspects: Bluetooth headphones (200 ms or more by
   themselves), large PipeWire buffers behind BASS's ALSA output (osu!
   players on Linux lower them with `PIPEWIRE_LATENCY`), and the describer
   lagging (cause 2 below). A delay that differs from one start to the
   next would also explain symptom 1: a play whose real delay is smaller
   than the calibrated one lands early. Each start should log the audio
   output's and the microphone's reported latency, so this can be read
   instead of guessed.

**Narrowed down (owner, same day):** the same v0.1.2 AppImage plays as
expected on the owner's desktop, and capping the Deck's power (TDP 6 W)
or not makes no difference. That rules out the package and the CPU, and
points at the Deck's audio path: its built-in speaker and microphone,
which SteamOS runs through PipeWire filters (echo cancellation, noise
suppression). Those add a delay that varies, and a noise suppressor fades
out a steady tone such as a held ocarina note.

**Common cause found in the code (2026-11-14).** The owner also reported
that choosing a microphone in the selector sometimes takes two tries
before the meter moves, and that the desktop's own microphone never shows
any of this. Two faults explain all of it:

- BASS keeps the recording device *per thread*, and a thread that never
  chose one records from the lowest device already opened. `MicSource`
  named the device only when one was chosen by hand, so "the default"
  meant whatever device the opening thread last used. Plays and
  calibration open the microphone on pool threads, so which device a
  play heard changed from one start to the next. The Deck lists several
  inputs (the built-in microphone, its PipeWire filters such as echo
  cancellation, monitors); a desktop with one microphone never shows it.
  A restart opens on another thread and may land on another device:
  this is why restarting the level "fixed" the timing for a while.
- The selector's choice was never kept: it fed only its own meter, and
  plays and calibration always asked for the default.

Fix: the device is always named and set on the opening thread (the
default resolved to its index), the choice is kept in the ruleset's
settings (`Microphone`) and used by plays, calibration and the editor's
recorder, and each open logs which device it got. The stream-clock
mapping (cause 1 below) stays to do: it makes any remaining start-up
delay or gap harmless.

**Held notes fade on the Deck's default input (owner, 2026-11-15).**
With the selection fixed, a held ocarina note is heard at first and its
level then falls while it is still played, on "Default microphone"; on
"acp5x" (the raw input) it does not. The default input runs PipeWire's
processing for calls. Input names change with software and hardware
updates, so the game cannot pick by name. Instead:

- *Check my microphones* in the microphone step: every input listens at
  once while the player holds one note (`HeldNoteCheck`, tested
  offline), and the one that keeps the note steadiest is chosen and
  kept. The current one stays unless another holds the note 6 dB better.
- While the player plays in that step, a held note that loses more than
  9 dB at the same pitch brings up a hint to run the check.
- A kept choice that is no longer among the inputs is not forgotten: the
  step says so and the game uses the default until the player checks.

**"Microphone busy" on the next level (owner, 2026-11-15).** With
"acp5x" a whole level played as expected; the next one said the
microphone was busy and never listened. A raw input takes one recording
at a time, and a level let the microphone go only when its screen was
disposed, late and in the background; the next level gave up at the
first refusal. Now a level lets it go as soon as it ends, a busy
microphone is tried again every 0.5 s for up to 15 s ("Waiting for the
microphone…"), and the calibration's other steps open the chosen
microphone too.

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
