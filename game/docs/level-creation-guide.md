# Making a kan! level: the level creator's handbook

A level in kan! is a song's score: the notes a player plays, and the music
that plays along with them. This handbook walks the whole path, from a song
file on your computer to a level someone else can play. It is written for
musicians. You do not need to program anything.

- **Status:** first draft, 2026-11-11, written against the editor as it is
  today. Screenshots are placeholders for now (each says what it should
  show) and will be filled in from the real editor.
- **The editor is kan!forge**, inside the game. Open it on a level and the
  tabs along the top are: **setup**, **track**, **timing**, **compose**,
  **design**, **verify**. This handbook follows that order.
- **Words used here:** a **level** is the playable unit; the **companion**
  is the music that plays under the player (the accompaniment); the
  **reference** is the full performance the notes are transcribed from;
  **level audio** is the final mixed companion, exactly what plays during
  the level. The full glossary is in
  [editor-pipeline.md](editor-pipeline.md) §6a.
- **Screenshots:** each `<!-- screenshot: … -->` note marks where a picture
  of the current editor belongs.
- **This handbook is never finished.** Corrections and additions are very
  welcome: open an issue or a pull request (see "Improvements welcome" at
  the end). A slightly stale handbook beats none.

For how a level is judged when it is played, see
[gameplay.md](gameplay.md). This handbook is about making levels.

## What a level is

Five things make a level:

1. **The reference**: the full performance of the song. It is one track,
   optional, and it is what the transcriber listens to. It is not part of
   the level audio.
2. **The companion**: the tracks that play under the player, mixed
   together. This can be a separated accompaniment, a recording you made
   yourself, or nothing at all (a level can be played with only the score).
3. **The score**: the notes the player plays. Each one has a start time, a
   pitch (a note name such as G4, plus cents), a duration, an intensity
   (0 = softest, 1 = loudest) and a playing style (normal, legato,
   staccato, accent, and vibrato). The score is what gameplay judges, so it
   is the heart of the level.
4. **Arrangements for instruments**: when a level is played, kan! fits it
   to the player's instrument: the whole level moves by octaves into the
   instrument's range, and any note still outside folds in by octaves. The
   level also declares what it was written for (Setup, **Instrument**,
   "played on"). Writing a score that suits more than one instrument
   family is the level maker's craft; see the golden rule under
   [Sharing](#sharing).
5. **The look**: colours, sparkle, glow and how the screen behaves at
   rests and at the climax. It lives in the **design** tab and makes the
   level feel like the song.

A level is only as good as its score and its companion mix. Both are made
in kan!forge, and everything you do is saved on your own computer. While
you work in the editor, nothing is uploaded, and the microphone is used
only when you ask for a section recording.

## Getting a song in

### What you may use

kan! is "by the community, for the community", and a level is meant to be
shared. So the song must be free to share:

- **Both the tune and the recording matter.** A public-domain melody in a
  copyrighted recording is not usable. The tune and the audio both need to
  be public domain, CC0, CC BY or CC BY-SA. NC and ND licences are out.
- **Where to find material:**
  [charts/docs/free-song-candidates.md](../../charts/docs/free-song-candidates.md)
  is a researched list of songs that are free to distribute, with a source
  link, the exact licence and a suggested credit line for each. It also
  lists the traps (songs that look free and are not).
- **Your own recording of an old melody is the simplest case.** Record the
  melody, write or render your own accompaniment, and the rights are
  yours to give. The candidates list calls this "route (b)".
- **Credit the performers.** Covers are often made for the love of it.
  Setup has a **Cover credits** section for exactly this: where the cover
  was published, each performer's **name** and **what they did**, their
  links (channel, social networks, support pages), and **a message of thanks
  (optional)**. The song's artist still goes in **Metadata**; this credits
  the people who played the version you used.

<!-- screenshot: the setup tab, showing Metadata, Cover credits, Instrument and the Resources rows -->

### Local files only

kan! works on files on your computer. You pick audio in the setup tab's
**Resources** section and it is copied into the level. (A URL field exists
in development builds only, as tooling for the developers; it is not part
of the flow described here.)

In **Resources**:

- **Audio tracks** is the one place audio enters the level. Click it, pick
  a file, repeat for more. The same file is never added twice.
- **Video provides the audio**: when a video's audio is the music, turn
  this on and the video's audio joins the set as a companion track. Leave
  it off to use a video for visuals only.
- **Audio Track (level audio)** shows what gameplay will play: the mix of
  the companion tracks. It is rendered automatically (see below).
- **The set** lists every track that has been imported or created.
- Setup also holds **Instrument** ("played on"): what the level is played
  on. It tells the game how to listen (one note at a time or several) and
  the transcriber what to listen for. Set it before you transcribe.

## The track tab: roles, separation and edits

The **track** tab is where tracks are arranged into a set. Its own two
views are **Arrange** and **Mix the companion**.

### Roles

Every track wears a chip or two. There are two roles:

| Chip | What it means |
|---|---|
| **companion** | Plays under the player during the level. The companion tracks, mixed, are the level audio. There can be many. |
| **reference** | The full performance (one per level): what gets transcribed, and what plays along with the companion in the song-select preview. |

There is no "music" or "accompaniment" role, and **demux** is not a role:
it is an action (see below) that a track can be queued for. Role-bearing
tracks are highlighted, and each track's waveform is its row's background,
so the set is readable at a glance.

A track can wear both chips, but the usual shape is one clean take of the
melody as the reference and the rest as companion. "The full performance"
means the performance the notes come from: a solo take, or the cleanest
line in the recording. A separated melody stem is often the best reference
you will ever have.

<!-- screenshot: the track tab with two or three tracks, their waveforms, role chips and the Actions cards -->

### Separating a song into its instruments (Demux)

Most recordings mix the melody and the accompaniment together. The
**Demux** card separates a track into stems, on your own computer, so each
one can take its own role. Pick the tracks in the card's checklist and
press **separate**.

What separation does:

- It splits one track into stems (bass, drums, other, vocals, guitar,
  piano) that join the set as new tracks. Each can take any role, and can
  be edited like any other track.
- It takes time and shows its own progress, per track. The first run may
  also check the graphics device before it starts.

What separation does not do:

- **It is not a remix tool.** The stems are what the model hears; some
  instruments end up in "other", and a thin stem is marked **no meaningful
  audio?** and moved to the end of the list (keep it only if you know
  better).
- **A separated track is never demuxable again.** Neither is a stem nor a
  rendered mixdown: the card lists such tracks as "already separated" or
  "mixed material". Separate once, choose well.
- **Nothing depends on it.** Demux and transcription are both optional. If
  the song is one instrument already, use it as it is.

### Editing a track without losing anything

Editing is non-destructive: the file you imported is never modified. Press
**edit** on a track (or click its waveform) to open its volume, range and
edits. Drag a range across the waveform (or type **from (s)** and
**to (s)**), then choose what happens to it:

| Button | What it does |
|---|---|
| **keep this** | This part stays audible; everything outside every kept part goes silent. Timing is preserved. |
| **silence this** | The range goes silent; everything else stays. Timing is preserved. |
| **trim to this** | The saved file is cut down to this part. |

You also get **low cut (Hz) · 0 = off**, **high cut (Hz) · max = off** and
**pitch (semitones) · ±12 = an octave** (for a companion written for
another range). All of this is heard live while you play, and nothing is
written until you press **save as new track**: the edits are rendered into
a new track with a name of your choice, and the source is muted so the mix
still sounds the same. One Ctrl+Z brings it all back. **discard edits**
drops the pending work instead.

The typical case: one recording of two instruments becomes a clean lead
track (the **reference**, transcribed cleanly) and the other instrument's
parts as **companion**.

Each track also has a place in time. Drag the clip to move it (or type
**starts at (s)**), drag its sides to trim it (**trimmed**), and use
**Start and end of this track** for an exact window. Removing a track
(click its ✕, then click again) never deletes the file.

### The companion mix

**Mix the companion** is the mixing desk: per track, whether it is in the
song (**companion**), a fader, a meter and mute or solo; and the master.
It opens hearing the **level audio**, with **hear every track** as a
comparison. Mute and solo are listening tools; a muted track is left out
of the level audio too.

- **Loudness**: every level audio is levelled to the game's reference
  (-20 LUFS), so no level is much louder than the next. The desk shows
  where yours sits, and **-1 dB**, **0 dB**, **+1 dB** nudge it (up to
  ±12). This is a taste setting, not a mastering tool.
- **Level audio start and end** sets the window the level plays in. The
  preview plays only that window, and the render trims to it.
- The level audio is rendered automatically: when the companion mix
  changes, it updates when you leave the track tab or save. There is no
  manual render button. The **Level audio file** card on the track tab
  says whether it is **Up to date** or **Changed: it updates by itself when
  you leave this tab or save.**

### Undo

Ctrl+Z and Ctrl+Y undo and redo everything on this tab: roles, volume,
mute, positions, edits, removals, saves and stems. The set saves itself
about a second after each change; **save** (or Ctrl+S) saves it and the
level at once.

## The compose tab: the score

The **compose** tab is a piano roll over the song. Time runs left to right,
pitch goes up, and the grid underneath is the tempo you set. The score is
saved in the level (`kan.score.json`), so it travels with it.

### Start from a transcription

If you have a reference, run **Transcribe** on the track tab first (pick
the reference in the card, set **instrument (what the transcriber listens
for)**, press **transcribe**). The result is a draft: a first reading of
what the performance plays. In compose, press **start from the
transcription** to make it the score.

A draft is a starting point, not truth: **trust it, then fix it**. The
transcriber is good at when a note starts and roughly how long it is;
pitch is where it needs your ears. It is deterministic (the same file and
instrument always give the same notes), so nothing piles up when you run it
again.

Without a reference, write by hand: pick the **draw** tool and click to add
notes, or click the keyboard on the left to hear a pitch first (sliding
along the keys sounds each one). The **notes** toggle (P) plays the score
itself, so you can compose by ear against the companion.

<!-- screenshot: the compose tab with a transcription on the roll, the toolbar above, the inspector on the side -->

### Fixing notes

Select a note (click, Ctrl+click, or drag a box) and the inspector shows
what gameplay will judge:

| Field | What it is |
|---|---|
| **pitch** | A note name such as G4, F#3 or Bb5. |
| **start (s)** | When the note begins. Drag the note, or drag several as a group (they keep their shape). |
| **duration (ms)** | How long it lasts. Drag a note's right edge. |
| **intonation (cents)** | How far from the equal-tempered pitch the note is meant to be (0 = in tune). |
| **intensity** | How strongly the note is played: 0 = softest, 1 = loudest. |
| **playing style** | normal: a plain note / legato: joined to the previous note / staccato: short and detached / accent: played stronger. |
| **vibrato** | A gentle, regular wobble in pitch while the note is held. |

Arrows nudge the selection (↑/↓ a semitone, Shift for an octave;
←/→ one grid step), Delete removes, Ctrl+C / X / V copies, cuts and
pastes. **snap** keeps notes on the beat grid; hold Shift while dragging to
place freely. **undo** and **redo** (Ctrl+Z, Ctrl+Y) cover everything.

### Notes kan! is unsure about

The transcriber sometimes hears a note but cannot tell its pitch. That note
is **not thrown away**: its timing is real. It is drawn grey with a **?**,
given its nearest neighbour's pitch, and kept until you decide. Press **U**
to select them all, then set the **pitch** field (which clears the mark) or
delete them.

This is the general rule for drafts: what kan! has not measured is
**unjudged**, never "wrong". Nothing is judged before it is measured, so a
quiet or uncertain note never counts against anyone.

The verify tab lists these notes ("This note's pitch (…) is only a guess:
the transcription could not hear it. Set it in compose."), and the MIDI
export leaves them out of the corrected score.

The transcriber can also report one sound twice. **remove repeats**
(Shift+U) takes out the copies and is undoable.

### Tempo and the grid

The tempo bar shows the tempo at the playhead: **BPM**, **beats per bar**,
**first beat**, and **tap** (tap along and the tempo follows you).
**change** puts a tempo change at the playhead; **remove** takes the one
you are in out (the first tempo always stays). **beat here** fits the grid
to the music: the tempo before the playhead stretches (at most half again
or half) so its nearest beat lands where you clicked. The notes stay where
they sound, always. **the beat is** **½** / **×2** counts the same music
in another note value, for a level timed in quarters whose pulse is whole
notes.

When a performance breathes (a cover played without a click), the job is
yours: kan! proposes no tempo map. Put a tempo change wherever the music
moves, then use the repair tools below to bring the audio back to the grid.

**timing**, the tab before compose, is osu!'s own tempo-point editor and
gives more control over the same grid.

### When the recording itself drifts

The **Section** panel (right side of compose) sets a section of the level:
**start here**, **end here**, or **use selected notes**. With a section
set, **Repair the audio's timing** fixes the recording to the grid:

| Button | When to use it |
|---|---|
| **insert silence at the playhead** | A part came in too early: everything from here on plays later by the length. |
| **cut the section out** | A part came in late, or a pause is too long: the section is taken out and what follows closes the gap. |
| **play the section in the length** | A part sped up or slowed down: it plays faster or slower to last exactly the length, its pitch kept. |
| **fit the section to the grid** | The same, but to the nearest whole number of grid steps. |
| **sync with the next tempo** | The next tempo change arrived off the beat: the panel says by how much and puts in (or cuts) that much at the change. |

The length is a share of a beat (**1 beat**, **1/2**, **1/4**, **1/8**,
**1/16**, triplets when the grid has them) or typed in as **custom ms**.
One repair moves every track of the set together (the companion and the
reference), and the notes, the design's sections and the later tempo
changes move with the music. One Ctrl+Z undoes all of it.

Sections also hold music you want to redo: **record with the mic** plays
from just before the section and replaces its notes with what you play;
**re-transcribe from the reference** transcribes that part again. Both
replace only the section's notes and leave them selected.

Two numbers in the same panel affect timing: **mic delay (ms)** (how late
notes played into your microphone arrive; **measure the mic delay** on a
few clicks measures it) and **transcriber delay (ms)** (how late the
transcriber places notes it finds). Section recordings are placed with your
mic delay in mind, so measuring it once is worth the minute.

<!-- screenshot: the compose tab's Section panel, with the repair tools and the Timing block -->

### Keeping a level playable

**Keep a pitch range** (compose side panel) removes notes outside a span,
for example a piano's right hand (keep G4 to A8). Removed notes are listed
by pitch with **add back** buttons, so nothing is lost. Notes outside your
own playable range are greyed in the roll: they may still suit other
players, which is fine.

## The design tab: the look

The **design** tab decides how the level looks while it is played. Its
sections cover the whole level without gaps: intro, phrase, verse,
pre-chorus, chorus, bridge, solo, break, outro, each with a name of your
choice.

Press **Automatic design** and kan! cuts the level into sections by itself:
silences of about three and a half seconds or more become breaks, the music
between them becomes phrases, and the busiest, loudest phrase becomes the
chorus and the **climax**. A level nobody designed already looks right; the
automatic design follows the score as it changes, until you edit a section
by hand.

For each section (**This section** panel):

- **what it is**: the kind, shown as the section starts. A break is a rest:
  nothing to play.
- **name (optional)**: shown instead of the kind, e.g. "Second verse".
- **climax**: the level's high point: hotter colours, bigger sparks, the
  background glows with the music.
- **while there is nothing to play**, for rests: **the music as light, then
  3-2-1 to the next note**, **the music as light**, or **a calm, empty
  screen**.
- **colours of this section**: leave it on the level's palette, or give the
  section its own.

For the level (**The whole level** panel): **palette** (thirteen, from
Sakura to Nature; grade colours stay the same in every palette),
**sparkle** (hit effects, 0-2), **glow** (how much playing well lights the
screen, 0-2), **picture** (how much of the background shows behind the
staff) and **show section names**.

The timeline shows the sections over the notes: click to select, drag a
boundary to move it. **Split at playhead** and **Join with next** reshape
them; **Undo** and **Redo** cover the changes. **Save into level** writes
the design into the level (it is also saved when you leave the tab or the
editor). While you work, **companion** (M), **reference** (R) and
**notes** (P) let you hear the level as a player will.

<!-- screenshot: the design tab with the section timeline over the notes and both panels -->

One more reason to design well: the **Practice** mod loops the level's
sections ("From section" / "To section" in the mod's settings), so clear
section names ("Verse 1", "Chorus") are how players practise your level.

## The verify tab: the checklist

The **verify** tab checks a level before it is shared. Kan!'s own checks
replace osu!'s (which count objects a kan! level does not have); osu!'s
other checks (preview point, background, metadata) still run. Clicking an
issue with a time takes you there.

| Check | What it means and how to fix it |
|---|---|
| **The level has notes** | "The level has no notes yet: write them in compose, or start from a transcription." A problem: a level needs notes. |
| **Notes the instrument plays** | A note outside the usual range of the level's instrument: "not every player reaches it." Either move it (transpose the phrase), or accept it and know the level suits that instrument. |
| **One note at a time on a one-note instrument** | Two notes sound at once on an instrument that plays one at a time (ocarina, flute, recorder, wind, bowed). Overlaps under 20 ms are a legato's seam and fine; a real overlap needs one note removed or shortened. |
| **Notes with a known pitch** | "This note's pitch (…) is only a guess: the transcription could not hear it. Set it in compose." Fix the pitch or delete the note. |
| **Notes within the level audio** | A note starts after the level audio ends: it can never be played. The audio is cut short: extend **Level audio start and end** on the track tab, or move the note. |
| **Not much audio after the last note** | The audio runs on past the music. Keep an outro that adds to the song, or set the level audio's end in the track tab. |
| **The level audio is the mix, at its loudness** | "The level audio is older than the mix" means it re-renders when you leave the track tab or save. A message with the level's LUFS and "its peaks kept it from levelling" means it will sound louder or softer than other levels (see **Loudness** in the mixing desk). |
| **A tempo grid** | No tempo yet: compose has no beats to snap to and the staff no bars. Set the BPM in compose or the timing tab. |

Warnings are not failures: a note outside the usual range may be exactly
what you intend. The checklist exists so that nothing in a shared level is
a surprise.

## Sharing

### What a level is made of

Inside the level travel the files kan! writes: the score
(`kan.score.json`), the design (`kan.design.json`) and the cover credits
(`kan.credits.json`). The companion mix is the level's audio file. Your
imported tracks, stems and work files stay on your computer (in the
kan!forge work folder) and are not part of the level.

### Sharing today

Export the level from kan!forge (the editor's **File** menu, **Export**,
**For compatibility (.osz)**) and you have a file anyone with kan! can
import. Reaching kan!'s own catalogue is still manual: the maintainer
imports it (the upload flow is planned; see `docs/issues/34`). Send the
file, or a link to it, and the credits you wrote.

Formats to come: `.kan` (a song package) and `.kchart` (lightweight
per-instrument arrangements) are the community formats being specified in
[charts/README.md](../../charts/README.md). Nothing to do today; your
level's score and design are already the data those formats will carry.

### The golden rule

**Every level must play on more than one instrument family where the music
allows.** kan! fits a level to the player's instrument by octaves when it
is played, and song select lists a level to the players whose instrument
can play it (its notes in range, or an octave away). So a score that stays
within a reasonable span (about an octave and a half) and avoids effects
only one family can do will serve an ocarina, a recorder, a flute, a
violin, a guitar. Before you call a level done, play it (or have it
played) on a second instrument family, and use the verify tab's
**Notes the instrument plays** check as a guide to what nobody reaches.

A calm level that plays on many instruments is worth more than a brilliant
level only one person can play. kan! is calm by design.

## Worked example: Sakura Sakura, from song to level

A small, freely licensed song, end to end. **Sakura Sakura** is a
traditional Japanese melody, public domain everywhere, about one octave
(D5-D6 as charted in the candidates list), and gentle: a good first level.
The candidates list's Sakura Sakura row has verified free recordings with
their exact licences and credit lines, and a public-domain score and MIDI.
Here we use a recording of the melody with a small accompaniment, released
under a free licence (for example the CC BY recording named there), and
turn it into a level played on the ocarina.

<!-- screenshot: the finished level playing: the staff, notes with names, the companion behind -->

### 1. Import (setup tab)

Open the editor on a new level. In **Resources**, click **Audio tracks**
and pick the recording. Leave **Video provides the audio** off. In
**Metadata** set the title ("Sakura Sakura") and the artist ("traditional").
In **Cover credits**, credit the recording as its licence asks (the
candidates list gives the exact credit line for each recording, of the form
"performer, licence, via Wikimedia Commons"), with the link to where it
was published. In **Instrument**, set **played on** to the instrument the
melody is played on (ocarina): this steers the transcriber and tells the
game how to listen to players.

<!-- screenshot: setup after import: the file under "Audio tracks", Cover credits filled in, Instrument set to ocarina -->

### 2. Separate (track tab)

Go to the **track** tab. The recording is one track with everything in it.
Open the **Demux** card, tick it, and press **separate**. When the stems
arrive (bass, drums, other, vocals, guitar, piano), give them roles:

- the stem that carries the melody becomes the **reference** (exactly one);
- the stems that should play under the player become **companion**;
- mute anything that adds nothing (a stray drums stem on a folk melody,
  for example).

<!-- screenshot: the track tab after separation: stems as rows, the melody stem with the reference chip, companions with the companion chip -->

The original track stays in the set, unmuted and without roles. Mute it, or
leave it; it is not part of the level audio unless it is a companion.

### 3. Transcribe (track tab)

In the **Transcribe** card, pick the reference (it becomes the reference),
set **instrument (what the transcriber listens for)** to ocarina, and press
**transcribe**. The card keeps its outcome: "Transcription ready from '…'".
The draft's notes are drawn on the reference's lane and offered in compose.

### 4. Correct the score (compose tab)

Open **compose** and press **start from the transcription**. Now work with
your ears:

1. Press **companion** (M) and **notes** (P) and listen. The score is
   heard as plain tones over the companion; **reference** (R) silences the
   companion and plays the full performance in its place.
2. Fix the **?** notes first: press **U**, set each **pitch**, or delete
   what was noise.
3. Press **remove repeats** (Shift+U) in case the transcriber doubled a
   note.
4. Set the tempo: **tap** along with the song in the tempo bar, and use
   **beat here** where the grid and the music disagree. Where the
   performance breathes, put a **change** in.
5. Where the recording drifts off the grid, set a section and use
   **fit the section to the grid** or **sync with the next tempo**.
6. Nudge what still sounds wrong: drag notes in time, resize their
   duration, set the **playing style** where the music asks for legato or
   accents.

<!-- screenshot: compose mid-correction: a grey "?" note selected, the inspector open with the pitch field -->

Save with **save** (Ctrl+S); it is also saved when you leave the tab.

### 5. Design (design tab)

Open **design** and press **Automatic design**: Sakura Sakura has a phrase
structure the automatic design reads well, with a breath between phrases
and an obvious chorus. Give the climax phrase a **name** (for example
"Chorus"), pick a **palette** (Sakura suits it), keep **sparkle** and
**glow** at 1 unless you want more, and press **Save into level**.

<!-- screenshot: the design tab on the example: the sections over the notes, the chorus marked as climax -->

### 6. Verify (verify tab)

Open **verify** and read the checklist. On this level, expect:

- guessed pitches, if any **?** notes were left (fix them in compose);
- **One note at a time on a one-note instrument**, if a separation
  artefact doubled the melody (remove or shorten one);
- **Not much audio after the last note**, if the recording's applause or
  silence runs long (set the level audio's end on the track tab).

Fix what matters, accept what is intentional, and the level is ready.

### 7. Play it

Play the level yourself. Then the golden rule: hand it to a player of
another instrument family (a recorder or a guitar, for this one) and watch
them play it. Their notes of confusion are the next edit.

Export with **File**, **Export**, **For compatibility (.osz)**, and share
it.

## Improvements welcome

This handbook describes the editor as of 2026-11-11. kan!forge grows, and
this document should grow with it. If something here is wrong, unclear, or
missing, please say so: open an issue in
[KairoPhos13/kan-rhythm](https://github.com/KairoPhos13/kan-rhythm/issues)
or send a pull request (English only, see
[CONTRIBUTING.md](../../CONTRIBUTING.md)). Level creators who do not write
code are exactly the readers this document is for, so corrections in plain
words are as welcome as patches.
