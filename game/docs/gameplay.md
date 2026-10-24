# Gameplay

The first playable kan!: a level plays the score written in the compose tab,
the player plays a real instrument into the microphone, and the game judges
and shows each note. Design intent (owner discussion 2026-10-14): learning
over pure reaction, a reward that is the music itself, no game over, and
feedback in musical words.

## At a glance (how a play works today)

The rules as they stand; the sections below give each one in detail, with
the reason it was chosen and what it replaced.

- **The score** is the level's `kan.score.json`: each note's pitch, start,
  length and playing style.
- **Listening.** The microphone opens off the game's thread; libkan
  describes what it hears (attacks, pitches, a reading every 10 ms).
  `ListeningTimeline` places each heard moment on the level, taking off the
  mic delay and the player's timing habit. A microphone that stops giving
  sound is opened again.
- **A note's try** is the attack, within its window, nearest a perfect in
  time × pitch. An early attack waits for a nearer one; a held sound is one
  try (its swells are no new attacks); the previous note still sounding, a
  chord mate or the next note come early are no wrong tries.
- **Timing windows** are a share of the note's *pace* — the time to its
  nearest neighbour — not of the tempo grid: perfect ±20 % (50–120 ms),
  good ±45 % (135–450 ms), meh ±80 % (240–800 ms), and never past halfway
  to a neighbour.
- **Pitch** is judged with timing: each grade is an ellipse in time × pitch
  (perfect 35 cents, good 50). Within a semitone a hit is the note (at
  least a meh); past it, another note. An attack's pitch is graded once it
  settles (a wind's first milliseconds), or by the note's centre. The
  instrument's own tuning is learnt and taken off.
- **Score.** Perfect 300, good 200, meh 100, miss 0; accuracy is their
  average. Rank: SS 100 %, S ≥ 95, A ≥ 90, B ≥ 80, C ≥ 70, else D; an S or
  SS needs no miss. Misses drain health; an empty bar fails the level.
- **Mods.** Easy and Hard Rock widen or narrow the windows; Hidden fades
  the notes; By heart shows each only for a moment; Wait for me stops at
  each note until it is played; Practice loops a section; Half Time and
  Double Time change the speed.
- **The staff** shows about five notes ahead at the pace of what comes
  (1.6–3.2 s). The level ends when its last note ends.

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
  libkan describes; the game judges (`LiveJudge` lives in `core/Play`).
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
  - **Never below 0 ms** (owner 2026-10-19): microphone, driver and
    processing can only make a note heard later, so `MIC_DELAY_MS` has a
    minimum of 0 (a saved negative loads as 0). A measurement below 0 says
    so — "your notes landed 38 ms before the clicks: that is you playing
    ahead, not the microphone" — and offers 0 ms at most; the last play's
    correction stops at 0 too. Isolated check: answers 40 ms early measure
    −38 ms and get that message.
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

## Judging (`Kan.Core.Play.LiveJudge`)

The judge listens **blind**: libkan's expected mode would help per note,
but it does not work streamed yet and accepts octave-below expectations
(measured in [transcription-quality.md](transcription-quality.md)).

- A score note is answered by the closest unclaimed onset within its meh
  window that has the note's pitch (the grades below).
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
- **Time and pitch are judged together** (owner 2026-10-22: "out of tune
  and late may make a meh, a good in pitch and a perfect in time something
  between … generalist, not hard-coded to my instrument"). A grade is an
  ellipse in time × pitch: its timing window (below) on one axis, a pitch
  budget on the other — perfect 35 cents, good 50 (`PitchToleranceCents`
  and `PERFECT_PITCH_SHARE` 0.7 of it; Easy 52.5 / 75; the perfect was 25
  until 2026-10-23: "a little more lax for a perfect by pitch, without a
  bigger ellipse"). The first ellipse a hit lies in is its
  grade; in tune the ellipses are exactly the timing windows. Any hit in
  the meh window **within a semitone** (`ACCEPT_CENTS` 100) is the note,
  at least a meh, whatever notes are around (owner: air pressure moves a
  wind a semitone either way, and learning to place it should not cost the
  streak); past a semitone it is another note. Pitches are read after the
  instrument's learned tuning is taken off, so a detuned instrument plays
  fair. Among several hits, the nearest a perfect in time × pitch is taken
  (a slip a semitone off loses to the right note a moment later). The
  words name the cost: early/late only off the perfect's window,
  sharp/flat past the perfect's 35 cents. Wait mode releases on the same semitone.
  Re-judging the owner's play of 2026-10-22 (its pitches: 73 % within 25
  cents, 23 % 25–50, 2 % beyond): misses 21 → 16, perfects 226 → 147.
- **The readings settle a pitch that has not come yet** (2026-10-23: a
  C5 tongued out of an F5 took the F's leftover as its provisional pitch,
  and libkan's final pitch — the centre of the whole note — came only when
  the 738 ms note ended, past the judge's wait: "not heard"). A heard note
  whose pitch is not final is the score's note once its readings hold it
  for 160 ms (`SETTLE_READINGS` 16, about one cycle of a 5–7 Hz vibrato),
  starting within 200 ms of the attack and before the next one. Every
  reading path counts a reading within a semitone, unless it is nearer
  another note the score has around (a semitone neighbour still sounding
  is that neighbour). The pitch graded is the readings' median: a vibrato
  is graded by its centre, not its swings. libkan also stopped reading a
  tongued note's pitch in the dip before it speaks (measurement
  invariants, "A tongued note is read where it speaks"). Re-judged, that
  C5 is a meh (114 ms early, 50 cents sharp), and the streak holds.
- **The microphone never holds the game** (owner 2026-10-23: with the
  disk saturated by an update, a quick restart froze 5 s opening the
  microphone, which then stopped giving sound 1.6 s in, and the level stood
  still until the window lost focus). It opens off the game's thread
  (`LiveSession.Open`); listening begins once it is open, from that moment
  (`Begin` drops what arrived meanwhile, so the first sample is still "now"
  and the mic delay keeps its meaning). "Opening the microphone…" shows
  after a second. It closes off the game's thread too, and the play's
  recording is kept after. A microphone gives samples all the time, silence
  included: one that gives none for a second has stopped, and is opened
  again ("The microphone stopped for a moment: it was opened again.").
- **One sound, one try** (owner 2026-10-23: a C6 played 258 ms early and
  held swelled twice; each swell came back as an attack and the one 100 ms
  late was taken: "good · late" for an early note). An attack that only
  goes on with an earlier one of the same pitch — no note took it, it is in
  this note's window, and the readings held that pitch between them with
  no break over 50 ms (`SAME_SOUND_GAP_MS`; a stopped note leaves ~150 ms)
  — is no new try, as in osu!. A repeated note is no such case: its
  previous attack was the previous note's.
- **An attack's pitch may settle** (owner 2026-10-23: an ocarina's first
  milliseconds take air pressure to find the pitch, "and it even sounds
  nice"). The first pitch libkan reports reads ~45 ms of the attack. When
  it is out of a perfect's budget (and the timing leaves room for one),
  the judge waits for the readings: every run of 160 ms (16 readings, one
  vibrato cycle) that starts within 200 ms of the attack, before the next
  one, is a settling, graded by its median; the nearest of these and the
  first pitch is graded. A final pitch is the note's centre already. The
  wait ends at most 460 ms after the attack. Another note settles as
  nothing: the ellipse's outer bound (a semitone) is unchanged, so a wrong
  note is still told and punished. The owner's play of 2026-10-23 (provisional
  vs final: n2 +34 → +2, n35 −48 → −2), re-graded with this, the 35 cents
  and the ±20 %: 23 → 42 perfects, 73 % (C) → 88 % (B); with their timing
  habit corrected too (median −27 ms, "Your timing"), 90.5 % (A).
- **An early attack waits for a nearer one** (owner 2026-10-23: early
  notes felt judged harder than late ones). The judge used to take the
  first attack that answered a note, at once. A late attack has no rival
  after it, but an early one did: an attack on the line, just after, lost
  to it (and a tongue's cut, heard as an attack, once decided a note before
  its start). Now an early attack that is no perfect is taken only once
  the line is as far behind as the attack was ahead (plus the 100 ms an
  attack takes to be heard), or once the pitch of a nearer attack came.
  Perfects are still taken at once.
- Misses drain health and an empty bar fails the level, as in osu!
  (owner 2026-10-15; it was no-fail before). "No fail" and "wait for me"
  keep it going.
- **Accuracy and rank as in osu!, not osu!mania** (owner 2026-10-23:
  a no-miss run of 25 perfects, 19 goods and 12 mehs was a C at 71 %).
  A meh is still the right note, so it weighs as osu!'s ok: perfect 300,
  good 200, meh 100, miss 0 (`KanScoreProcessor`; osu!mania's meh is 50).
  Rank from accuracy: SS 100 %, S ≥ 95, A ≥ 90, B ≥ 80, C ≥ 70, else D;
  as in osu!, an S or SS needs no miss (one miss: A at best). "Wait for
  me" scales the points (0.02), never the accuracy or the rank.
- **Grades: perfect, good, meh** (owner 2026-10-21: "five are too many —
  less is more"; osu!mania's great and ok are gone), each **a share of the
  note's pace** either side of it ("it should sound good, not stress"):
  the time from its start to the nearest other note's start, before or
  after (`LiveJudge.PaceOf`; notes within 30 ms are a chord; in a score
  with no rests, a note's length). Perfect ±20 % (50–120 ms), good ±45 %
  (135–450 ms), meh ±80 % (240–800 ms), set in `KanBeatmapConverter`
  (`KanHitObject.PaceMs`). Notes a beat apart at 120 BPM: ±100, ±225,
  ±400 ms. A note alone: a 500 ms pace. Until 2026-10-23 they were a share
  of the tempo grid's beat, but a beat is how the music was written, not
  how fast it goes: the owner's level, timed at 130 BPM in quarters where
  its pulse is whole notes, would have judged four times apart had it been
  timed right ("the current system is no use"). Fast notes are now judged
  tighter, slow ones looser, never past ±120 ms for a perfect (an attack
  further off is heard early or late however slow the song). The perfect
  was ±15 % of the beat until 2026-10-23 (owner: early notes that "sound
  good humanly" were goods).
  **No window reaches past halfway to a neighbour** (the previous or next
  note, chord mates aside): in eighths at 121 BPM every window stops at
  ±124 ms, so a skipped note cannot take its neighbour's breath, and a miss
  is told before the next note comes. A note is called not heard 100 ms
  after its window closes (`LiveJudge.HEARING_MS`: libkan confirms an onset
  some 30–90 ms after it sounds). Before (same day) they were osu!mania's
  five steps, ±45/80/115/150/200 ms; re-grading the owner's first full
  normal play (325 notes) with these: perfect 103 → 149, good 74, meh 3,
  and 26 hits more than halfway to a neighbour (22 of them 130–195 ms
  early) become misses. Wider than osu!'s: a breath is no button. The
  results list only these (`KanRuleset.GetValidHitResults`). Words at the
  line: "perfect", "good · early", "meh · late", … Only the attack is judged
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
  by the result type: perfect 300, good 200, meh 50); what lowers it is a
  mic delay that is off.
- **The level ends when its last note ends** (owner 2026-10-23: the
  results came up at the last attack, over a long final note). osu! shows
  the results once every judgement is in, and a note is judged at its
  attack. The note that sounds last carries a nested `KanLevelEnd` at its
  end (`IgnoreHit`, scores nothing; the converter marks it), and its
  drawable stays alive until then. Results come 1 s after the end.

## Mods (owner 2026-10-15)

Mods that need nothing from the level editor:

| mod | what it does in kan! |
|---|---|
| Easy / Hard Rock | timing windows ×1.4 / ×0.7 (`LiveJudge.WindowScale`); Easy also accepts a note up to 75 cents out of tune instead of 50 (`LiveJudge.PitchToleranceCents`; a 65-cent-sharp player: 47 great + 3 meh of 50 on Easy, all wrong without it). Since 2026-10-22 these are the good's pitch budget, the perfect's 0.7 of it (see time × pitch above) |
| No Fail, Sudden Death, Perfect | as in osu! |
| Half Time / Daycore, Double Time / Nightcore | the level slower or faster, same notes (pitch kept): practise at 75 % or 90 % |
| Hidden | notes fade out on the way to the line (gone 300 ms before it) |
| By heart (BH) | each note shows only for a moment as it comes into view (500 ms by default, the mod's "Seen for" setting, 150–2000 ms), then is gone until played; no next-note marker; score ×1.12, ranked at 500 ms or less (ranked mods are otherwise only at their defaults, as in osu!), not with Hidden (owner 2026-10-23: Hidden showed them how much better they play by ear, "but to learn the songs I need to see the notes less — to play them practically from memory") |
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
scores — and "Practice · Verse · loop 3" shows at the top. With no
score to rank, the in-game leaderboard is off (owner 2026-10-19:
`KanDrawableRuleset` sets the player's `ShowLeaderboard` to false before
the HUD shows it, and the practice bar swallows Tab, so the setting is
not flipped for the next scored play). Combines
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

**The player is heard in their replay** (owner 2026-10-22: "in the replay
I cannot hear myself, only see how I scored"). A live play records what
the microphone hears (`PlayTake`: tapped on the describer's thread, a
low-pass and every third sample — 16 kHz mono, about 2 MB a minute),
with marks of where the level was as it was heard, placed as the judge
places onsets (the mic delay) but without the player's timing habit, so
the replay sounds as it was played. The take is kept when the level ends
(so a replay watched straight from the results finds it), raised to a
fixed peak (at most +20 dB), in `kan-forge/takes/{id}.wav` + `.json`;
its id rides in the replay as a `Take` frame (two 16-bit halves in the
coordinates). Kept: the last 20 plays and the best play of each level
(owner 2026-10-22); only on this computer. The replay plays it with the
level audio (`TakePart`, kept in place by `ChannelSync`), at the player's
own pace under rate mods; silent where nothing was played (before
listening, a pause, a wait). Verified isolated (a file played in real time
as the player): the take lands where the judge placed the notes (within
10 ms of its mean offset), and the replay found and played it.

**Rewinding a replay** (owner 2026-10-22): the notes the judge decided
after the new time are undecided again (osu!'s result revert →
`KanPlayState.Unreport`), and the staff draws the notes behind the new
time again (its first-visible cursor restarts when the time goes back),
so their effects play as they did the first time.

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
- **The perfect window, shown** (owner 2026-10-22: "make the circle on the
  line glow gold while a note arriving would be a perfect"; then "it covers
  too much — put the effect on the vertical lines and drop the circle"):
  the now line turns a light, bright gold and widens while an attack played
  now would be graded perfect, and the window's note's grid line is gold too.
  The attack is placed as the judge places what it hears (the player's
  timing habit included) and graded on the note's own window (the beat's
  share, Easy/Hard Rock's scale, narrowed by the neighbours;
  `LiveJudge.WithinPerfect`, tested against `Judge` offset by offset). The
  gold fades within 60 ms of the window closing, shows through a hit's
  kick, and ignores verdicts (an early decision does not cut it short). The
  width tells it from a palette's warm "hot" line colour. A note head's
  centre is the note's start: the window is centred on it unless the player
  timing moves it.
- **The pulse** (owner 2026-10-22, after Sayonara Wild Hearts' colour flip,
  "without being so psychedelic"): the grid's lines alternate between two
  quiet tones (teal, violet), one line each, so the colour changes as each
  line crosses the now line. A note on the grid is played on time on the
  change; the gold window sits around it.
- **Bar lines travel with the notes** (owner 2026-10-22: "the only way to
  know the bars is listening… only the main ones, not what is between"): a
  faint line across the staff where each bar starts, its number above,
  fading in on the right and out just past the now line. Between them,
  shorter lines centred on the staff (owner 2026-10-22: "show the
  in-betweens as shorter lines… the smallest shown should be the smallest
  in the piece"): each beat, and each part of a beat as fine as the notes
  need (`Bars.DivisionOf`: halves, triplets, sixths… from where the notes
  start; a part only a stray note or two uses, or unquantised notes, do
  not count). Bars as compose's grid draws them (`Kan.Core.Score.Bars`,
  tested; the same bars for Page Up / Page Down). A tempo that breathes
  changes on any beat, so a timing point landing on a beat of the tempo
  before (within a quarter beat) goes on counting the bar it falls in
  (owner 2026-10-22: the tempos were synced but "the lines don't show
  it"); a change of beats per bar, or a point off the beats, starts a bar
  as osu! counts every timing point. osu!'s own timing tab still restarts
  the bar at each point (fork, unchanged).
- **The notes look** (owner 2026-10-15: "not osu!'s level yet in effects,
  smooth transitions, clarity, flashes, colours"): lit beads in the
  palette — lighter at the top, a thin white rim, a soft halo — with
  **white names** (shadowed, readable on every colour). Every change plays
  from the moment it happened: a hit pops (+50 %), flashes white and
  turns its grade's colour over 160 ms, then settles a little smaller; a
  miss shudders, sinks and turns into a shadow (black, half seen). Grade
  colours: perfect white radiating faint gold (a lasting halo), good green,
  meh grey. **Timing and pitch apart** (owner
  2026-10-21: the player sees what they got right): a played note's ring
  takes the timing's grade (white = perfect), its inside turns white when
  the pitch was in tune (within 25 cents), else keeps the note's colour. Notes fade and grow in at the right
  edge and fade towards the left; a held tail fills white with light
  running along it.
- **Reading the notes** (owner 2026-10-15: "note indicators aren't clear
  enough"): heads are bigger with a readable name; the **next note** to play
  wears a white rim and is the only one with the approach ring, while later
  notes step back with distance (dimmer up to ~3 s ahead), so the eye lands
  on what comes first. A **marker on the now line** ("G5 ▸") shows where the
  next note will cross it and glides from note to note: the trail meets it
  when the note is played right (hidden with the Hidden mod). A **target
  circle** on the line at the same height (owner 2026-10-19: easier than
  judging by eye when a note is on the line) is a little wider than a note:
  the moment to play is when the note sits centred inside it; its inside
  lights up over the note's last half second. It stays on a note being held
  until the note ends (owner 2026-10-21: jumping ahead distracted), then
  moves on; into a slurred (legato) note it glides over the held note's
  last moments, as the pitch slides. Every natural
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
  for a perfect (18 / 11 / 4 sparks for perfect / good / meh), and the line kicks
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
  see it"). The time shown ahead does not depend on the size.
- **The staff zooms to the notes** (owner 2026-10-23: "the shorter the
  notes, the wider the horizontal scale … the more they are squeezed, the
  harder they are to play right"). The time shown ahead is about five
  notes at the shortest pace coming in the next 3.2 s (`KanHitObject.PaceMs`),
  between 1.6 and 3.2 s, eased over ~0.8 s: fast passages come in
  wider, slow ones as before. The owner's level: 2.3 s for its notes
  461 ms apart, 3.2 s for its long ones.
- Everything is drawn from fixed pools, so any length of score costs the
  same per frame.

## Stutters (owner 2026-10-19)

`KanHitchMonitor` (on the playfield) logs every frame over 25 ms during a
play — `kan!: hitch at 12.3 s — update / draw / audio ms; GC pause and
collections; describer ms per 10 ms block, ms of audio waiting` — and the
play's totals when it ends (`kan!: play frames — …`: frames over 12, 25
and 50 ms, the longest GC pause in one frame). The describer's load
comes from `LiveSession.TakeLoad` (worker: slowest block, biggest backlog).
Read the log after a play that stuttered before changing threads or
priorities: the update, draw, audio and describer threads already run apart.

First finding (2026-10-19): frames were smooth, but the describer stalled
up to 75 ms on one 10 ms block, so judgements landed late. The cost was
libkan's blind content classifier (`classify_content`: mono or chord), which
re-measured its whole pitch grid on every acceptance pass, one harmonic at
a time. It now measures the grid once and runs a series' harmonics side by
side, bit-identical (libkan `note_stream_test` fixture 15). On the owner's
ocarina take, the worst block fell from 97 ms to 6 ms (p99 30 → 5 ms; none
over 10 ms).

Second look (2026-10-19, "micro-stutters" at 180 Hz): 50 ms let the
owner's 35-40 ms frames through — at 180 Hz a frame is 5.6 ms — so the
threshold is 25 ms and the summary counts the slow frames. The GC runs
~25 gen0 collections a second in play: osu!'s high-performance session
puts the GC in `LowLatency` (small, frequent gen0 collections; upstream
by design), so every allocation per frame counts at 360 fps. The staff
named its notes and lines with new strings, and fonts, every frame; names
are now spelled once (`KanNotation`), fonts built once, the interlude's
"next" text set only when it reads differently. The first verdict of a
play stuttered ~33 ms: the feedback font's glyphs load before the first
note now (7 → 0.4 ms); most of the rest is first-run JIT in osu!'s
judgement path (`ApplyResult` ~9 ms), once per play.

## Play trace (owner 2026-10-21)

"The trail lands on the note on time, yet the perfect comes late or a meh
comes": every live play writes `kan-play-<date>.csv` to the logs folder
(`KanPlayTrace`, the last 20 kept, written in the background). Rows: each
heard onset (stream time, where it was placed on the level, when it
arrived — a placement after the arrival is cut to the arrival), each pitch
fact and reading, each verdict with the onset it took (`LiveVerdict.PlayedId`;
empty when timed by the readings, a slur) and how long after the note's
start it came, slow frames, the level clock against real time, waits. The
summary line adds the clock gap and how many onsets were placed after
their arrival.

## Placing what is heard on the level (owner 2026-10-21)

The trace of a real play showed 311 of 522 onsets placed *after* they
reached the game: the stream was anchored to the level once, when
listening began, and the level's clock then sat 0-40 ms off real time, so
those onsets were cut to their arrival (~70 ms after they sounded — the
describer's confirmation) while the rest kept their own time; two notes
played alike graded apart. Now every frame remembers what the level's clock
showed at that real moment (`KanPlayState.remember`/`levelAt`), real time
starts with the stream's first sample, and a sound is placed at the level
time shown when it sounded (minus the mic delay). Isolated runs: 0 onsets
placed after their arrival (was 19 of 19).

**Player timing** (`ForgeTiming.PLAYER_TIMING_MS`, the loader's Microphone
group, saved in timing.json): the player's habit, negative when ahead of the
line — a wind player's attack starts before they hear the note. Their notes
are taken that much later. "Set your timing to …" after a play corrects it
(it corrected the mic delay before, which cannot go below 0). Files played
as the player (dev) ignore it.

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
| `KAN_DEV_SEEK="20:8"` | with `replay`: 20 s in, seek the level to 8 s (a replay rewound; 2026-10-22 it crashed: a frame's negative elapsed time sent the staff's eased pitch range to infinity — every easing now takes the step's size) |

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

