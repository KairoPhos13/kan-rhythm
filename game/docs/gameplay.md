# Gameplay MVP (owner 2026-10-14)

The first playable kan!: a level plays the score written in the compose tab,
the player plays a real instrument into the microphone, and the game judges
and shows each note. Design intent (owner discussion 2026-10-14): learning
over pure reaction, a reward that is the music itself, no game over, and
feedback in musical words.

## Data flow

```
microphone (BASS capture, 10 ms periods)
  └─ MicSource: lock-free ring, the callback only copies
       └─ LiveSession worker thread
            └─ kan_forge_live_process (libkan NoteDescriber, as offline)
                 └─ facts: HIT, PITCH (provisional/final/unresolved), CLOSE,
                    FRAME (pitch reading every 10 ms)
                      └─ KanPlayState (update thread)
                           ├─ LiveJudge  — pairs onsets with score notes
                           ├─ trail      — the player's pitch on the staff
                           └─ bloom      — how open the music is
```

- **Score → hit objects:** `KanBeatmapConverter` reads `kan.score.json`
  from the level (or the editor's newer autosave) through `KanLevelFiles`.
  The ruleset icon, loaded by the toolbar at start, hands it the game's
  storage. A level without a score keeps the old placeholder.
- **Live = offline:** the bridge drives the describer exactly as
  `transcribe_audio` does (128-sample blocks, measurements drained after
  each), so the selftest checks that live onsets equal the offline ones.
  libkan describes; the game judges (`LiveJudge` lives in `forge/Play`).
- **Time:** a fact's gameplay time = the clock when listening started +
  its stream time − the mic delay (`ForgeTiming.MIC_DELAY_MS`, measured
  with the compose tab's mic calibration, which has the same structure:
  the take's first sample is "now").

## Judging (`Kan.Forge.Play.LiveJudge`)

The judge listens **blind**: libkan's expected mode would help per note,
but it does not work streamed yet and accepts octave-below expectations
(measured in [transcription-quality.md](transcription-quality.md)).

- A score note is answered by the closest unclaimed onset within ±200 ms
  that has the note's pitch. Great ≤ 70 ms, ok ≤ 130 ms, meh ≤ 200 ms.
- The pitch arrives after the onset (provisional ~45 ms, final ~200 ms),
  so a note waits up to 300 ms past its window for it.
- A heard note whose pitch can't be told counts as at most "meh" (owner
  2026-10-15: noise must not pass for playing well). A score
  note with an unknown pitch accepts any note on time.
- Each played note answers one score note: a repeated note needs its own
  attack.
- **No onset, but the pitch arrived:** the describer marks a slur's new
  note only after ~250 ms, and very soft attacks (ocarina) may mark none.
  So when no onset answers, the pitch readings decide: if the note's pitch
  sounds for most of [start − 80, start + 150] ms, the note counts, timed
  by when the pitch arrived. For a legato note that is all it takes. For
  any other note, the pitch must not have been sounding just before
  (start − 200 … − 60 ms): a note held over doesn't answer a repeated
  note.
- **Out of time with only a provisional pitch:** the miss says which
  note was played ("F5 — play G5").
- Misses drain health and an empty bar fails the level, as in osu!
  (owner 2026-10-15; it was no-fail before). "No fail" and "wait for me"
  keep it going.
- **Known limit:** osu! stamps a result with the time it is applied, so the
  results screen's timing graph includes the pitch wait. Our verdict's
  `OffsetMs` is the real offset; it drives the in-game feedback.

## Mods (owner 2026-10-15)

Mods that need nothing from the level editor:

| mod | what it does in kan! |
|---|---|
| Easy / Hard Rock | timing windows ×1.4 / ×0.7 (`LiveJudge.WindowScale`) |
| No Fail, Sudden Death, Perfect | as in osu! |
| Half Time / Daycore, Double Time / Nightcore | the level slower or faster, same notes (pitch kept): practise at 75 % or 90 % |
| Hidden | notes fade out on the way to the line (gone 300 ms before it) |
| Wait for me (WT) | the level stops at each note until it is played (it keeps listening from the HUD, since osu! updates nothing inside a stopped ruleset); score ×0.1, cannot fail |
| Auto / Cinema | a perfect performance, played as a replay |
| Muted | as in osu! |

Not applicable: Relax, Auto Pilot, Spun Out (no cursor or spinners).
Later: Flashlight (needs its own shader), Score V2.

Rate mods: the player's sound arrives in real time and the level runs at
the rate, so a fact's level time is `anchor + (stream − waited − mic
delay) × rate`.

## Replays

A kan! replay holds what the judge heard, not keys (`KanReplayFrame`):
each heard onset, pitch and pitch reading, stamped with the level time it
reached the judge and its own delay. A replay (and autoplay) feeds those
facts to the same judge instead of listening to the microphone, so it
gives the same verdicts. Legacy `.osr` encoding: the button field holds
kind × 100000 + delay, the coordinates hold the values. Verified: a full
recorded play and its replay from the saved score gave the same 325
verdicts (offsets within 1 ms, from whole-ms frame times).

## Song select preview (owner 2026-10-15)

The level audio is the companion only, which alone is "too abstract" as a
preview. `Kan.Forge.Play.LevelPreview` (a game-wide component started by
the ruleset icon) adds the part the player will play, on its own channel,
while song select or the loading screen after it shows a kan! level:

- the **reference** take when it is on this computer (the level's
  author has it: `kan-forge/<set>/set.json`), placed, trimmed, filtered
  and muted as in the editor — it sounds best;
- otherwise the level's **notes** as plain tones (`ScoreSynth`, rendered
  in memory, a little under the companion).

It follows the preview's position, fades and rate mods (Half Time plays
the part at 75 % too). Both channels report positions the same way, so
`ChannelSync.ExpectedOffsetMs = 0` corrects a late start instead of
keeping it as an offset, and learns the seek latency (measured in the
isolated instance: ~1 ms apart after two corrections at the start).

## The screen (`KanStaff`, `KanPlayfield`)

- **Living staff:** time runs right to left onto a fixed "now" line, and
  height is pitch. Guide lines are drawn for the natural notes: the treble
  staff's five lines are brighter and every C is named. The shown range
  follows the next few seconds of music, eased.
- **A note** has a head with its name, a tail as long as it lasts, and a
  mark for its style: accent `>` with a bigger head, staccato `•` with no
  tail, a legato line to the previous note, and `~` along the tail for
  vibrato. A ring closes on the head as it reaches the line.
- **Trail:** the player's pitch every 10 ms. It is green while on a
  sounding note, white otherwise, so a miss shows why (above = sharp,
  after the line = late). A hit note's tail fills while its pitch is held.
- **Feedback** at the line: "on time", "a bit early/late", "· sharp ↑ /
  flat ↓", "pitch unclear", "F5 — play G5", "not heard".
- **Bloom:** playing well opens the music. The level audio goes through a
  low-pass filter that lifts with the bloom (fully open near the top);
  a miss is heard at once: the music muffles hard (towards 200 Hz) and drops
  in volume, recovering over ~0.9 s. The background brightens
  with it. Real stem layering (the band joins in) needs the stems in the
  level: a later step.
- **The now line is the reward** (`KanNowLine`, owner 2026-10-15: "one
  of our best resources to feed the dopamine loop"). A hit lands on it: a
  ring and sparks burst where the note met the line, with a white flash
  for a great (14 / 8 / 4 sparks for great / ok / meh), and the line kicks
  wider in the hit's colour. A streak heats the line: wider, golden, a
  wider soft glow (fully hot at 32). Milestones (10, 25, 50, 75, 100, …)
  send a shockwave across the staff and "25 in a row" low on the line.
  A sustain held at its pitch burns at the line and sheds sparks. A miss
  cools it at once: red, a short shake, dull sparks falling.
- Everything is drawn from fixed pools, so any length of score costs the
  same per frame.

## Testing without a microphone or a screen

`Dev/KanDevPlay` (inert unless its environment variables are set):

| Variable | Effect |
|---|---|
| `KAN_DEV_PLAY=<beatmap set guid>` | opens that level and plays it unattended (no focus needed, no pause on focus loss) |
| `KAN_LIVE_INPUT=<wav>` | a recording plays the part of the microphone, paced by the game clock |
| `KAN_LIVE_INPUT_OFFSET_MS=<ms>` | where that recording starts on the level's timeline |
| `KAN_DEV_SCREENSHOTS=20,45` | screenshots that many seconds into the level |
| `KAN_LIVE_INPUT_REALTIME=1` | the recording plays in real time, like a player (rate and wait modes) |
| `KAN_DEV_MODS=HT,NF` | play with those mods; `AT` plays through a replay player |
| `KAN_DEV_SCREEN=select` / `replay` | open song select on the level / play back its newest local score |

A dev run never opens the microphone: without `KAN_LIVE_INPUT` it listens
to nothing. The test instance needs its own `/tmp` (mount namespace):
.NET's single-instance socket lives at `/tmp/CoreFxPipe_osu-framework-osu-lazer`,
so with only `unshare -n` it hands over to the user's running game.

Run it on a virtual display with its own data folder, so it touches neither
the user's screen nor their game: headless weston, `XDG_DATA_HOME` pointing
at a copy of the data, and `unshare -rn` (osu's single-instance IPC). Each
play logs one line, for example `kan! play: 325 of 325 notes judged — great …`.
In dev mode a heartbeat is logged every 5 s.

**First measurement (2026-10-14):** the ocarina level played by its own
reference recording. The owner's corrected score sits a median 99 ms
before the transcriber's onsets: the notes were aligned with the song by
hand. That is the transcriber delay the timing tab can now apply. With the
recording shifted by −99 ms, onsets alone gave 224 hits (offline
prediction from the same transcription: 231). After pitch arrival was added,
the level scores 261 of 325 (great 221, ok 7, meh 33; mean offset
−11 ms). The 64 misses are mostly notes corrected by hand (pitch or place)
that the raw recording does not play that way.

**After the libkan fixes (2026-10-15, `main` 2b114bc + 3e6ca04, merged):**
the same play gives 316 of 325 notes (great 289, ok 24, meh 3; 3 wrong,
6 not heard, 5 pitch unclear; mean offset +8 ms), up from 261.

