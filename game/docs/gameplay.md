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
  its stream time − the mic delay (`ForgeTiming.MIC_DELAY_MS`, the
  player's, kan-forge/timing.json).
- **Microphone, before playing** (owner 2026-10-17: "it consistently marks
  me late"; real plays landed +29…+67 ms, about +45 on average, over ~25
  practice rounds with the delay measured in compose): the player
  loader's "Microphone" group (`KanMicSettings`) holds the delay, a
  measurement and the last play's timing.
  - **Measure it on clicks** opens `MicTimingOverlay` (forge/Play; compose
    opens the same one — section recordings use the delay too): four count-in
    clicks, then eight to answer, a ball coming to the ring on each (the
    exact moment to attack, besides the sound), each answer marked beside
    the ring. `MicCalibrator` measures it the way a play hears: the clicks
    on a track, the live describer (`LiveSession`, the play's own path —
    compose's measured a recorder and an offline transcription), onsets
    placed on the track's clock as a play places them on the level's. The
    overlay is focused, so the level does not start meanwhile; the song
    about to start is silent while it is open (owner 2026-10-17: whichever
    track is playing, and the level's part with it — `LevelPreview` follows
    the track's whole volume, adjustments included). The measurement is
    raw (the whole delay); the marks and the words show it as a play would
    with the delay in use ("with your mic delay (41 ms) your notes landed
    22 ms late: the right delay is 63 ms"), and "Use" saves the whole
    delay, never adds to it (owner 2026-10-17: with a delay set, the marks
    still landed late). Isolated checks: a take answering exactly on the
    clicks measures 3 ms, 8 of 8, ±1; one 60 ms late measures 63 ms, then
    "it is right" with 63. Each answer is marked beside its own click;
    count-in answers grey and short (they never count — only the first
    note near each of the eight clicks does; a take playing all twelve
    measures the same 3 ms). Owner 2026-10-17: count-in notes, marked
    against the first click to play, looked like they spoiled it.
  - **Known limit:** answering clicks, people play ahead of them
    (negative mean asynchrony, tens of ms — more with a ball announcing
    each); in a song they read and tend to land late. The owner's own
    measurements came out −5…−73 ms while plays landed ~+86 ms in all. A
    loopback mode (the mic hears the speaker's clicks, no player) would
    measure the device alone, but not with headphones; for now the last
    play's timing is the closer measure.
  - **The last play:** the median lateness of a play on the microphone
    (not a wait mode, ≥ 10 notes; `KanPlayState.LastPlay`), and a button
    to set the delay to the one played with + that, as osu!'s offset
    suggestion does — for when the player knows they played it in time.
  - Dev: `KAN_DEV_SCREEN=mictiming` measures in the player loader, the
    clicks answered by `KAN_LIVE_INPUT` (never the microphone).

## Judging (`Kan.Forge.Play.LiveJudge`)

The judge listens **blind**: libkan's expected mode would help per note,
but it does not work streamed yet and accepts octave-below expectations
(measured in [transcription-quality.md](transcription-quality.md)).

- A score note is answered by the closest unclaimed onset within ±200 ms
  that has the note's pitch. Great ≤ 70 ms, ok ≤ 130 ms, meh ≤ 200 ms.
- The pitch arrives after the onset (provisional ~45 ms; final when the
  played note ends — measured 2026-10-17, not ~200 ms as assumed before),
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
- **A wrong try is a miss at once** (owner 2026-10-17: "it feels like
  'found it!' and the penalty at the same moment"): an attack made for the
  note (from 100 ms before its start) whose pitch is wrong and confirmed.
  Confirmed means libkan's final pitch, or the pitch held 150 ms (15
  readings in a row): libkan sends the final pitch only when a note ends,
  so a wrong note held until the right one is found was confirmed at the
  very moment of finding it. A slip put right before it is confirmed is
  no try (the right note wins).
- **The notes around are no wrong tries** (`LiveJudge.IsWrongTry`): an
  octave off (the detector's slip), the previous note still sounding (a
  typical learner's lag: the right note a little late is still good or
  meh, the streak kept), a chord's other notes, the next note come early
  (the score's notes within ±200 ms, `KanPlayState.ExpectedAround`).
  Alone they leave the note "not heard" at the window's end, not "wrong".
- Misses drain health and an empty bar fails the level, as in osu!
  (owner 2026-10-15; it was no-fail before). "No fail" and "wait for me"
  keep it going.
- **Grades: osu!mania's steps** (owner 2026-10-15: "chaining perfects is
  the vice"): perfect ±35, great ±70, good ±100, ok ±130, meh ±200 ms —
  great/ok/meh kept their bounds, perfect and good split them; wider than
  osu!'s because an acoustic attack is detected within ~20–30 ms. The
  results list only these (`KanRuleset.GetValidHitResults`). Words at the
  line: "perfect", "great", "good · early", … Only the attack is judged
  (timing and pitch); how long a note is held is not, so long notes at
  Half Time never need a full breath.
- **A perfect is heard:** `KanPerfectChime` rings a small bell
  (`ChimeSynth`, unit-tested) at the note just played, an octave up (at
  most C7) — the melody's own note, so it never clashes; game samples, so
  the effects volume governs it.
- **A hit is stamped with the attack, not the decision** (fork F-17,
  owner 2026-10-17: the tortoise / hare meter always said "late"): osu!
  stamped a result with the time it was applied, after the pitch arrived,
  so the hit error meters and the results' timing graph added the
  detection wait (+50 ms or more). `DrawableKanHitObject` stamps the
  note's start + the judge's offset. Accuracy was never affected (it goes
  by the result type: perfect/great 300, good 200, ok 100, meh 50); what
  lowers it is a mic delay that is off (+45 ms late turns most "great"s
  past ±70 ms into goods and oks).

## Mods (owner 2026-10-15)

Mods that need nothing from the level editor:

| mod | what it does in kan! |
|---|---|
| Easy / Hard Rock | timing windows ×1.4 / ×0.7 (`LiveJudge.WindowScale`); Easy also accepts a note up to 75 cents out of tune instead of 50 (`LiveJudge.PitchToleranceCents`; a 65-cent-sharp player: 47 great + 3 meh of 50 on Easy, all wrong without it) |
| No Fail, Sudden Death, Perfect | as in osu! |
| Half Time / Daycore, Double Time / Nightcore | the level slower or faster, same notes (pitch kept): practise at 75 % or 90 % |
| Hidden | notes fade out on the way to the line (gone 300 ms before it) |
| Wait for me (WT) | the level waits at each note until it is played — a gentle walk for learning (see below); score ×0.02, cannot fail |
| Auto / Cinema | a perfect performance, played as a replay, and heard: the reference take when it is on this computer, else the notes as tones — one of the two, never both (owner 2026-10-15; `AutoplayPart`, shared with the song select preview as `LevelPart`) |
| Muted | as in osu! |

Not applicable: Relax, Auto Pilot, Spun Out (no cursor or spinners).
Later: Flashlight (needs its own shader), Score V2.

### Any level on any instrument (owner 2026-10-15)

The player's instrument (Settings → kan! → My instrument: a transcription
profile, with the lowest and highest notes it plays — sensible defaults
per instrument, adjustable) decides how the game listens (one note at a
time, or several) and the range a level must fit: the level moves by
the octave that keeps the most notes in range (`RangeFit`, unit-tested;
no move on a tie with staying put), and a note still outside folds in by
octaves. The staff shows, and the judge expects, the fitted notes. The
player loader's "Instrument & notation" group says it before playing
("Your instrument: guitar (E2–E4). This level: C5–D6 — plays 2 octaves
lower."), and the play says it once at its start. The level's own
instrument (Setup) says what it was written for. Known limit: a replay
plays back with the fit of the instrument set when it is watched.

"My instrument" is also the first control of that loader group (owner
2026-10-17: "it is not clear where the instrument is chosen" — it was only
in the game's settings). Its default is **As the level**
(`KanPlayer.AS_THE_LEVEL`, empty): the game listens for the level's own
instrument, in the level's own range (nothing moves). The default was
"keyboard", so an ocarina player was heard as a pianist without ever
choosing it. Below it, the lowest and highest notes it plays (owner
2026-10-17: "ocarinas come in many kinds"), hidden while it is "as the
level". Only the kind of instrument is asked — how it attacks, and one
note at a time or chords ("Ocarina · one note at a time", "Piano /
keyboard · chords too", `KanScore.IsMonophonic`) — and its range. The
instrument listened for is decided when listening starts (`KanPlayState`,
logged "listening as …"); the level is fitted when it is prepared (while
the loader shows) and fitted again as a live play starts if the
instrument or range changed meanwhile ("fitted again"; replays and
autoplay keep the fit their frames were made with).

**Song select lists the levels the instrument can play**
(`KanFilterCriteria`, the ruleset's own filter — no fork change): a
level's notes must sit in the range as written or an octave away
(`KanPlayer.CanPlay`: nothing folded note by note, which would change the
melody). On by default (Settings → kan! → Only levels my instrument
plays); off while the instrument is "as the level". Search `fits=no` for
the others, `fits=yes` for these with the setting off. Levels without a
score are always listed. Isolated checks on the ocarina level (C5–D6):
ocarina A4–F6 → shown; C5–G5 → hidden; guitar E2–E4 → shown (two octaves
down). Known limit: song select filters again when its filter changes,
not when the setting does.

### Practice (owner 2026-10-15)

Scored play keeps the genre's rule — earn the hard part by getting there
— and practice is apart from it, as in other music games: the Practice
mod (PR, score ×0, cannot fail) plays a range of the level's design
sections ("From section" / "To section", listed with their names and
start times in the mod's settings) over and over. The level is cut to
those sections' notes; 1.5 s after the last one the play restarts by
itself — before it could complete, so practice never fills the local
scores — and "Practice · Verse · loop 3" shows at the top. Combines
with the speed mods, Wait for me and Autoplay. Only sections with notes can be
practised: the strip leaves the others out and a range on one of them
(the intro — section 1 by default —, a break, the outro) snaps to the
nearest that has notes (it crashed, and the outro looped forever,
2026-10-16). A part later in the song starts at once, 2 s before its
first note (osu! starts at the beginning and offers Skip). The playable
sections are named in order within their kind — "Phrase 1, Phrase 2…
Chorus" (`DesignParts`, unit-tested) — on the strip, the title and the
mod's dropdown, which lists only them (owner 2026-10-17: "verse 9, verse
11" read as missing verses; the intro and breaks were being counted).

While practising (owner 2026-10-16: "how do I go to the next one, back,
choose?"): ← → (or [ ]) the previous / next section, Shift+← → a shorter
/ longer part, or a click on the strip of sections at the top (Shift+click:
up to that section). W toggles Wait for me, S steps the speed (100, 90,
75, 60, 50 %, pitch kept), L listens to the part played for the player
(the reference, else the notes — as autoplay), each a button too. The
part restarts at once; the picks last until the player leaves practice (the selected mods cannot change during play, so
it is kept apart from the mod's settings). The first play of a practice
starts from the selected mods (owner 2026-10-17: "it says wait for me is
off but it is actually waiting for me"): Wait for me → W on, Half Time /
Daycore / Double Time / Nightcore → that speed (one of S's steps; practice
adds only the tempo that makes up the difference, so the rate is never
applied twice), Autoplay / Cinema → "Listen: on (Autoplay)", which L
cannot switch off (the replay plays the part). While practising W decides
whether the level waits, even with the Wait for me mod selected (its
overlay stays quiet). osu!'s own retry (the quick retry key, the pause
menu) is a restart too: it keeps the part and the options (owner
2026-10-17: after it the play waited but no longer heard — the old play
had reset the options while the new one was half built). Each play takes
its options once (`KanModPractice.Play`) and the ruleset, HUD and track
all go by them. Dev: `KAN_DEV_PRACTICE=2-3`,
`KAN_DEV_PRACTICE_NAV="right@8,shift+right@18,w@4,retry@20"`.

### Instrument tuning (owner 2026-10-15)

An instrument a little off A440 ("almost a semitone sharp on every note,
consistently") plays the right notes: `TuningTracker` learns its tuning
while playing — the median of (reading − sounding score note) over the
last few seconds of readings, ignoring readings more than 0.7 semitones
from the current estimate (other notes, not tuning), clamped to ±80 cents
— and the play state takes it off every pitch and reading before judging.
The learnt value is kept in `kan-forge/timing.json` for the next level;
Settings → kan! shows it and can fix it by hand. A synthetic performance
45 cents sharp: learnt +45 in ~5 s; with it saved, 325 of 325 great in
wait mode, results shown.

### Wait for me (owner 2026-10-15)

- **A little grace:** the level runs 200 ms past a note before it stops
  (`KanPlayState.WAIT_GRACE_MS`); a note played on time is heard within it,
  so the level never stutters for the detection's own delay. While the
  mod is on, the judge never closes a note as missed: it is played, or
  waited for.
- **Released fast:** five readings at the right pitch (50 ms) release the
  wait, without waiting for a new note's pitch event — a held or slurred
  note has none. The level then picks up from the note's start (plus the
  detection's moment, 110 ms + the mic delay), so the next notes come in
  time with the player (synthetic player 3 s late: 54 of 54 great after
  the wait, mean offset −6 ms; resuming where it stood left them ~110 ms
  late). The time the level stood still is measured when it really moves
  again, track start latency included.
- **Not shown as a wait** (owner 2026-10-15: seeing the level run past
  the note and jump back "feels like a penalty"): the staff glides to a
  halt with the note on the line (`KanPlayState.DisplayTime`: at most
  20 ms past it, asymptotically) and only ever moves forward, easing back
  into step when the note is played; no "waiting" banner.
- **Not frozen:** osu! updates nothing inside a stopped ruleset, so
  `KanWaitOverlay` lives in the HUD: it keeps listening and draws the
  player's pitch live on the now line against the waited note — a dot
  with a comet of what was just played, a line to the target, "higher ↑
  (a tone)" / "lower ↓" / "that's it — hold it".
- **Not silent, not a loop:** `WaitHold` holds the music at the moment
  the level picks up again (`WaitResumeTime`, the note's start plus the
  detection's moment) as a sustained sound, so the song re-engages from
  exactly what was held (owner 2026-10-15: "keep the
  tone, don't replay a short stretch"; a looped beat came first). The
  last ~170 ms before that point are decoded from the level audio, and
  `SpectralFreeze` keeps their spectrum and resynthesises it with fresh
  random phases (4096-sample frames, overlap-add wrapped around the end):
  the same chord and timbre, no rhythm, no seam (unit-tested: pitch,
  loudness, seam). Half volume, low-passed at 3.5 kHz, faded in 150 ms
  and out 100 ms; silent while the pause menu is open.
- **The pause menu:** while it is open, what is played goes nowhere —
  not to the judge, not to the wait (owner 2026-10-17: the song went on as
  they played under the menu; `KanPlayState.UserPaused`, set by the wait
  overlay). Dev: `KAN_DEV_PAUSE=wait` pauses at the first wait.
- **Honest combo:** a wrong try (see Judging: confirmed, the notes around
  forgiven) makes the note a miss the moment it is confirmed — during the
  200 ms grace or while waiting (`KanPlayState.WrongTry`) — shown at once
  ("B5 — play F5 · combo lost"). The level still waits for the right
  note: the miss is the penalty, finding the note is still the walk
  (2026-10-17; before, the miss landed when the right note was found).
  Misses dip the music gently in this mode. Synthetic learner take: the
  previous note held 600 ms into the next → no penalty; a wrong note held
  450 ms → miss 150 ms in, then the wait, then the right note releases it.
  An on-time take with no mods: 58/58 perfect, no wrong tries.
- **One attack, one note** (2026-10-15: "the note after a wait passed
  without being played"): attacks heard while the level waits (or picks
  up) belong to the waited note and never count for the next one; a note
  at the same pitch as the one before it needs an attack of its own —
  holding the previous note on does not play it, and neither does its
  final pitch arriving late (a score overlap of the two is a tie).
  Synthetic checks: a skipped note followed by its repeat waits at both;
  a clean full run waits at none (325/325 great).
- **Jumps:** a level that jumps while listening (Skip, a practice part
  further on) is noticed as its time drifting from the real time since
  listening started; listening starts over from the new position (it
  kept placing what was heard from the old one, 2026-10-16).
- **Pause:** continuing from the pause menu while the level waits keeps
  it waiting (it ran on before).

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

## Design in play (owner 2026-10-15)

`KanScene` turns the level's design (editor-pipeline.md §6g; generated
from the notes when the level has none) into what the screen does: the
section on now, the palette in force — eased between sections over
0.7 s, never jumping — and how far the screen is into a rest or a climax.

- **Rests** (breaks, and an intro/outro with nothing to play): the staff
  steps back, and `KanInterlude` fills the space with the companion as
  light — its spectrum in mirrored bars, a soft core that breathes with
  it — and "next: A5 in 12 s". The rest lets go 2.6 s before its next
  note, so the note is seen coming; the last 3 s count 3-2-1 in a ring
  closing on where that note will arrive. A section can ask for a calm,
  empty screen instead.
- **Climax:** the line runs hotter, sparks are half again bigger, and the
  climax colour rises from the bottom of the screen pulsing with the
  music's low end.
- **Section names** show as they start (an automatic design names only
  its climax).
- **Picture:** a level's background picture or video shows behind the
  staff, the palette a veil over it (`KanDesign.Picture`, 0.6 by default,
  set in the design tab); playing well and rests show more of it. Before
  2026-10-15 the palette's background covered it entirely.
- **Palette and glow:** the background, notes, trail and the hot line take
  the palette; the design's glow scales how much playing well lights the
  screen, its sparkle the hit effects.

## The screen (`KanStaff`, `KanPlayfield`)

- **Living staff:** time runs right to left onto a fixed "now" line, and
  height is pitch. Guide lines are drawn for the natural notes: the treble
  staff's five lines are brighter and every C is named. The shown range
  follows the next few seconds of music, eased.
- **A note** has a head with its name, a tail as long as it lasts, and a
  mark for its style: accent `>` with a bigger head, staccato `•` with no
  tail, a legato line to the previous note, and `~` along the tail for
  vibrato. A ring closes on the head as it reaches the line. A sharp's
  head has eight points around it (owner 2026-10-17: so it reads as a
  different note at a glance, not only by "♯").
- **The notes look** (owner 2026-10-15: "not osu!'s level yet in effects,
  smooth transitions, clarity, flashes, colours"): lit beads in the
  palette — lighter at the top, a thin white rim, a soft halo — with
  **white names** (shadowed, readable on every colour). Every change plays
  from the moment it happened: a hit pops (+50 %), flashes white and
  turns its grade's colour over 160 ms, then settles a little smaller; a
  miss shudders, sinks and greys. Notes fade and grow in at the right
  edge and fade towards the left; a held tail fills white with light
  running along it.
- **Reading the notes** (owner 2026-10-15: "note indicators aren't clear
  enough"): heads are bigger with a readable name; the **next note** to play
  wears a white rim and is the only one with the approach ring, while later
  notes step back with distance (dimmer up to ~3 s ahead), so the eye lands
  on what comes first. A **marker on the now line** ("G5 ▸") shows where the
  next note will cross it and glides from note to note: the trail meets it
  when the note is played right (hidden with the Hidden mod). Every natural
  guide line is named at the left edge, the Cs brighter.
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
- **Notation** (owner 2026-10-15: "notes as letters are easy to
  confuse"): note names as letters (C D E), Do-Re-Mi, or none; and a
  colour per note — the twelve notes around the colour wheel (C red,
  D orange, E yellow…), whatever the palette — so a note is known at a
  glance on any instrument. Chosen in the player loader's side panel
  ("Notation", fork F-16) or Settings → kan!; followed live.
- **Size:** the staff (and the wait overlay) is designed for a 900-pixel
  high screen and scaled to the real one, times Settings → kan! → Note
  size (120 % by default; owner 2026-10-15: "on a 2K screen I can barely
  see it"). The time shown ahead stays the same at any size.
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
| `KAN_DEV_PAUSE=12` | pause 12 s into the level and continue 2 s later (as after a window switch) |
| `KAN_DEV_SCREEN=select` / `replay` / `design` | open song select on the level / play back its newest local score / open the editor on its design tab |

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

