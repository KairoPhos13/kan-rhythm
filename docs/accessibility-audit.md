# Accessibility audit: colour and text size (issues 18 and 19)

Audited 2026-11-12 on branch `agent/accessibility`. Scope: kan!'s own
surfaces, that is the ruleset (`game/ruleset`: the staff, notes, judgments,
HUD pieces, the notation settings, the instrument wedge) and kan!forge
(`game/forge`) where it encodes meaning by colour. osu!lazer's own screens
(including its HUD, score and health) are upstream's and only noted where
kan! draws into them.

Method:

1. A code audit of every site where colour carries meaning (below).
2. Numeric simulation of colour vision: the Machado, Oliveira & Fernandes
   2009 matrices at severity 1.0, distances in OKLab, contrast as the WCAG
   2.1 ratio. The maths lives in `game/core/Design/KanColourVision.cs` and
   every number below is asserted by `game/core/tests/KanColourBlindTest.cs`.
3. Static size analysis of every text surface (the sizes are in the code;
   effective on-screen sizes are computed from the client's scaling rules).

Screenshots of a running game were not possible in this lane (the game is
never launched here). The sweeps to produce them are wired in
(`KAN_DEV_NOTATION`, see the end of this document) and the palette views are
rendered offline in [accessibility-palettes.svg](accessibility-palettes.svg).

## 1. Where colour carries meaning

### 1.1 The ruleset (gameplay)

| Surface | Meaning | Colours (before) | Non-colour cue | Verdict |
|---|---|---|---|---|
| Judgment grade (`KanStaff.GradeColour`, the ring of a played note, the feedback word) | perfect / good / meh / miss | perfect `fffaf0`, good `7cdb8a`, meh `9e9ea8`, miss `ff5a6e` | the word itself ("perfect", "good", "meh", "not heard"), a pop vs a shudder and drop, ring thickness, the perfect's halo | Fine as states go; "meh" moved (see 2.3) because it read the same as the unknown-pitch grey |
| Pitch inside the bead (`KanStaff.NoteView`) | in tune vs off | white fill vs the note's own colour | **was colour-only**; now a mark too: "✓", "↑", "↓", "?" (`pitchMark`), and the feedback words "· sharp ↑ / flat ↓" | Fixed (issue 18's "never colour alone") |
| Note colour by pitch class (opt-in "colour by note", `KanNotation.Colour`) | which note it is (12 ways) | the rainbow table, C red to B pink | the note's name, a sharp's spiked head and its "♯" | Weakest surface of all (see 1.3); safe tables added |
| Level palette (`KanPalette` via `KanScene`) | notes to play, the player's pitch, the line's heat, the climax | the level's palette, 13 presets | each element's shape and place (beads, dots, a line, a glow) | Now overridable by the player (see 2.2) |
| The perfect window (`KanStaff.drawBars`, `KanNowLine`) | "play now" | `ffb300` gold against the palette's hot colour | the line widens (4 px vs 7 px), the window fades after 60 ms | Fine; the two golds are told apart by width and lightness |
| The grid's pulses (`KanStaff.drawBars`) | rhythm cue, decorative | teal `5fc9bf` / violet `9c86e6` | tick length (bar / beat / part) and bar numbers | Decorative only; no judgment rides on it |
| Now line heat, streaks, milestones (`KanNowLine`) | reward and streak | the palette's `Hot`, gold when hot | width, motion (kick, shake), "25 in a row" in words | Strong |
| The trail (`KanStaff.drawTrail`) | on the note vs not | the palette's `Trail` vs white | alpha 0.95 vs 0.45, position over the note | Fine |
| "Wait for me" pitch meter (`KanWaitOverlay`) | how far off the pitch is | green / amber / red | full sentences ("that's it, hold it", "higher ↑ (a tone)", "lower ↓"), dot position | Words carry it; the green and amber converge under protanopia (1.3) |
| Unknown pitch (`KanNoteColours` grey `8a8a99`) | "play the note" | grey | the name reads "?" | Fine |
| Status text (`KanPlayfield`) | messages | pink `ffb3c7` | it is text (11.4:1 on the background) | Fine |

### 1.2 kan!forge (the editor)

| Surface | Meaning | Non-colour cue | Verdict |
|---|---|---|---|
| Track roles (`ForgeRoles.Colour`) | companion vs reference | an icon and the words "companion" / "reference" everywhere roles appear | Fine |
| Spectrogram layers (`SpectrogramLayer`) | which track the energy is from | the layer toggles are labelled; intensity is alpha, one hue per layer (never a rainbow map) | Fine; the role tint is the only cue on the layer itself |
| Piano roll notes (`PianoRoll.NoteBlock`) | playable / out of range / unknown, intensity | every block prints its name and articulation glyphs; selection gets a white border; out of range is dimming | Strong |
| Level meter (`LevelMeter`) | output level: fine / hot / clipping | **bar height only** | Weakest editor site (green / yellow / red). Suggested follow-up: a numeric peak readout |
| Validation (`KanTrackRow`, `TagPill`, `KanActionCard`) | error / warning / hint | icons (× / ▲ / i) and the message text | Fine |
| Mute / solo (`ListenButtons`, `HearingLabel`) | what is heard | icon swaps and the reason in words | Fine |
| Selection, ranges, edit window (`WaveformPreview`, `TrackLane`, `WindowBar`) | what is selected | handles, labels, a white playhead | Fine |
| Recording section (`KanComposeScreen`) | recording vs selected | the tint is stronger; a status line | Acceptable (alpha-only difference, but two levels of tint plus text) |
| Design tab palette card (`KanDesignScreen.PaletteCard`) | what each palette slot colours | it is a swatch legend with a written hint | Now also warns when a palette's notes would sink into its background (see 2.4) |

### 1.3 The measured weak pairs, before and after

Closest two colours of a set, in OKLab distance after simulation
(0.02 is about "just told apart"; these were computed with the old values,
then re-asserted for the new ones in `KanColourBlindTest`):

**Note colours ("colour by note"), closest pair of the twelve:**

| Table | typical | deuteranopia | protanopia | tritanopia |
|---|---|---|---|---|
| as the level (before) | 0.163 (C vs B) | **0.004 (G vs G♯)** | 0.043 (G♯ vs A♯) | 0.036 (D♯ vs F) |
| deuteranopia-safe (after) | 0.088 | **0.089** | | |
| protanopia-safe (after) | 0.089 | | **0.090** | |
| tritanopia-safe (after) | 0.085 | | | **0.085** |

G and G♯ were the same colour for a deuteranope (they are neighbours on the
staff). Every safe table also keeps 0.08 clear of the unknown pitch's grey
and the perfect's white, and stays 0.08 apart for typical eyes too.

**Judgment states (perfect / good / meh / miss), closest pair:**

| | typical | deuteranopia | protanopia | tritanopia |
|---|---|---|---|---|
| before (meh `9e9ea8`) | 0.191 | 0.090 | 0.116 | 0.146 |
| after (meh `ccbfe5`) | 0.172 | **0.108** | 0.145 | 0.110 |

**Known weak pairs that words carry** (each is always drawn with its own
word or mark, never by colour alone):

| Pair | Worst vision | Distance |
|---|---|---|
| a miss's red vs the unknown pitch's grey | protanopia | 0.066 ("not heard" + shake vs "?") |
| "wait for me": right (green) vs nearly (amber) | protanopia | 0.070 ("that's it, hold it" vs "higher ↑ (a tone)") |
| "meh" vs the unknown pitch's grey, before | typical | 0.064 (twin greys; fixed by moving "meh") |

## 2. The palettes (issue 18)

### 2.1 The setting

One control: **Settings → kan! → Colour palette** (also in the player
loader's Notation group, where the level is started). Four options: *As the
level*, *Deuteranopia safe*, *Protanopia safe*, *Tritanopia safe*
(`KanPaletteMode`, `KanNotation.Palette`). It changes two things at once,
because both colour channels are the same decision for the player:

- the twelve note colours of "colour by note" (`KanNoteColours`), and
- the level's palette while playing (`KanPalette.ForMode`).

It persists with the rest of the kan! settings (the client's database via
`KanRulesetConfigManager`), and it follows live, like the other notation
settings.

### 2.2 The player wins over the level, without breaking the design

`KanPalette.ForMode` remaps a level's palette instead of replacing it:

- **Backgrounds are untouched.** The design's dark and lit ends, its mood
  and its picture veil stay exactly as the author set them (asserted).
- **Each role keeps its lightness**, clamped into its role's band (note
  0.60 to 0.75, trail 0.88 to 0.97, hot 0.80 to 0.90, climax 0.52 to
  0.72). A pale level like "holy" gets its notes darkened into the readable
  band; a dark one is left alone.
- **Each role takes the safe hue for its role**, and chroma never falls
  below the role's floor (so a white trail becomes a pale tinted one, and
  reads apart from the white off-note dots).

Worked example (the sakura palette; values computed offline, the game's
8-bit rounding may differ in the last digit):

| Role | as the level | deuteranopia-safe | protanopia-safe | tritanopia-safe |
|---|---|---|---|---|
| Note | `ff66aa` | `1ac194` | `899dfd` | `ac8ffd` |
| Trail | `b7ffcf` | `e9e9fd` | `fee3eb` | `fee2f2` |
| Hot | `ffd54f` | `dee25e` | `bdec78` | `1ff8f3` |
| Climax | `ff3d8b` | `3198ff` | `f841a5` | `ea4ac7` |

Verified over **every one of the 13 preset palettes** (this is what the
tests assert):

- the four roles stay at least 0.12 apart in OKLab under the vision they
  are made for, and at least 0.10 apart for typical eyes;
- notes, trail and hot keep at least 3:1 contrast against **both** ends of
  the background (worst case 3.42, before the override 4.28);
- white note names on the remapped note colours improve from a worst case
  of 1.21:1 (bright presets like cyberpunk's yellow) to 2.08:1, and every
  name carries a black shadow.

### 2.3 The judgment colours

The grades keep their colours in every palette (a player reads them the
same anywhere), so they were tuned once, globally. The owner's families
survive untouched: perfect is white radiating faint gold, good is green,
miss is clearly red. Only **"meh"** moved, from `9e9ea8` to `ccbfe5`
(a light lavender grey): the old grey was the same colour as the
unknown-pitch grey even for typical eyes (0.064 apart). The four states
are now at least 0.108 apart under every kind of vision (1.3).

Every judgment state is also told apart without colour, which is the
deeper rule of the issue:

- **a word**: "perfect", "good", "meh", "not heard", "F5: play G5";
- **a mark** on the judged note (new): "✓" in tune, "↑" sharp, "↓" flat,
  "?" when the pitch is unclear (`KanStaff.NoteView.pitchMark`);
- **motion**: a hit pops and settles, a miss shudders, sinks and becomes a
  shadow, and the now line jolts red.

### 2.4 A cheap check where the author is

The design tab's palette card (`KanDesignScreen.PaletteCard`) now shows
"the notes may be hard to read on this background" when a palette's notes
fall under 3:1 contrast against either end of its own background (the
verify tab's linter stays out of scope for now).

### 2.5 Before and after, seen through a simulator

[accessibility-palettes.svg](accessibility-palettes.svg) renders, offline:
the note tables before and after under each simulated vision (G vs G♯ is
the clearest collapse), the judgment states, and the sakura palette with
and without the override. Game screenshots (before and after, including
simulated CVD views) belong in the PR from the isolated instance; the
sweep for them is `KAN_DEV_NOTATION` (below).

## 3. Text size (issue 19)

### 3.1 One control, composed with the client's scaling

**Settings → kan! → Text size** (75% to 200%, default 100%; also in the
player loader's Notation group), persisted and followed live
(`KanRulesetSetting.TextSize`, `KanTextScale`).

It multiplies kan!'s own text and never re-applies a scaling the client
already does. Where the two meet is decided per surface:

- **Menu surfaces** (the instrument wedge, the notation and mic panels)
  sit inside osu!'s scaling hierarchy, where osu!'s own UI scale (Graphics
  → UI scaling) already reaches them. They take the kan! factor on top of
  it (`KanTextScale.MenuFont`).
- **Gameplay surfaces** (the staff's names, feedback words, markers, the
  wait overlay, the interlude, the practice overlay) sit in the playfield's
  scaling container, which the fork deliberately leaves out of osu!'s UI
  scaling (`Player.createGameplayComponents` →
  `ScalingContainer(ScalingMode.Gameplay)` → `applyUIScale: false`). They
  take osu!'s UI scale as well, here in kan!, so the player's choice is
  respected everywhere (`KanTextScale.GameplayFont`).

Window size scales gameplay text either way (the playfield draws on a
1024 by 768 canvas fitted to the window), so 4K and 150% operating-system
scaling were already comfortable; what was missing was a user factor and
the respect for osu!'s UI scale. One caveat is open: if a future isolated
run shows gameplay text growing twice at UI scale above 1.0, the fix is
one line (drop `OsuUiScale` from `KanTextScale.GameplayFont`).

### 3.2 Screen → control → range

| Screen / surface | Control | Range |
|---|---|---|
| Staff note names, line labels, bar numbers (`KanStaff`) | Text size (+ osu! UI scale) × screen fit × note size | 75% to 200% |
| Judgment feedback words (`KanStaff.onJudged`) | Text size (+ osu! UI scale) | 75% to 200% |
| Next marker, countdown, milestones (`KanStaff.Markers`, `KanNowLine`) | Text size (+ osu! UI scale) | 75% to 200% |
| "Wait for me" hints (`KanWaitOverlay`) | Text size (+ osu! UI scale) | 75% to 200% |
| Interlude "up next", section banner (`KanInterlude`) | Text size (+ osu! UI scale) | 75% to 200% |
| Practice overlay (`KanModPractice`) | Text size (+ osu! UI scale) | 75% to 200% |
| Instrument wedge (song select, `KanInstrumentWedge`) | Text size × osu! UI scale (client) | 75% to 200% |
| Notation and mic settings panels (`KanNotationSettings`, `KanMicSettings`) | Text size × osu! UI scale (client) | 75% to 200% |
| kan!forge tables and editors | osu! UI scale (client) only | 0.8x to 1.6x |
| First run and calibration screens (issues 16, 17) | not built yet; should use `KanTextScale` when they land |  |

### 3.3 What was checked at which size, and what was marginal

Sizes are the designed point sizes (a 1024 by 768 canvas for gameplay, the
client's own layout for menus). The physical size at 1080p is the designed
size times 1.41 (the window fit); at 4K it is times 2.81, or 1.41 logical
with 200% operating-system scaling, which lands on the same physical size.
A small laptop window at 1366 by 768 is the designed size times 1.0.

| Surface | Designed | 1080p 100% | 1080p 150% (OS) | 4K 200% (OS) | Marginal before? |
|---|---|---|---|---|---|
| Note names (14, next 16) | 14 px | 20 px | 30 px | 40 px | no (shadowed white) |
| Feedback words (17) | 17 px | 24 px | 36 px | 48 px | no |
| Line labels (13, alpha 0.22 to 0.55) | 13 px | 18 px | 27 px | 37 px | **yes**: size is fine, the faint alpha (about 1.6:1 against the background for the dimmest lines) is the readability limit, see below |
| Bar numbers (12, alpha 0.45) | 12 px | 17 px | 25 px | 34 px | **yes** on small windows; now grows with Text size |
| Milestone text (26) | 26 px | 37 px | 55 px | 73 px | no |
| Wait overlay words (22 / 18) | 22 px | 31 px | 46 px | 62 px | no |
| Wedge heading (18) and caption (~12) | 18 / 12 px | client-scaled | client-scaled | client-scaled | the caption, yes; now grows with Text size |

Judgment, mic-timing and settings text is either gameplay text (covered
above) or the client's own controls (covered by osu! UI scale). The
first-run and calibration screens of issues 16 and 17 do not exist yet.

**Note names and clipping.** Names are auto-sized text with no masking
anywhere on the staff, so "F♯4" or "Sol♯4" cannot clip at any Text size;
at 200% a long name can overlap its neighbours on a dense chord, which is
cosmetic and self-resolving as the note scrolls. Verified by construction,
worth one glance in the screenshot sweep.

**Not fixed here, flagged:** the faint guide-line labels (alpha 0.22) are
the one surface where contrast, not size, is the limit. Every line is
named more loudly on the notes themselves (white names), so nothing
gameplay-relevant is lost; raising the alpha floor is a one-line design
decision for the owner.

## 4. What this lane could not verify

1. **On-screen screenshots** (before/after, simulated CVD views of a
   running game, two scales). The isolated instance sweep is ready:
   `KAN_DEV_NOTATION="palette:level"` / `"palette:deuteranopia"` /
   `"palette:protanopia"` / `"palette:tritanopia"` and `"text:0.8"` /
   `"text:1.5"` with `KAN_DEV_SCREENSHOTS`, e.g.
   `KAN_DEV_PLAY=<set guid> KAN_DEV_NOTATION=palette:deuteranopia,colour,text:1.5 KAN_DEV_SCREENSHOTS=20,45`.
2. **The exact composition of osu!'s UI scale with the gameplay scaling
   container** (3.1's caveat). The code follows the fork's own opt-out
   flag; the isolated run decides.
3. **Real colour-blind players.** Simulation is a model. The palettes
   should meet a deuteranope and a protanope in issues 01 to 06's
   play-tests.
4. **Editor text sizes under the kan! control** (3.2's last rows): kan!
   Text size deliberately stops at the client's own scaling in kan!forge,
   so the editor tables only follow osu! UI scale.

## References

- Machado, Oliveira & Fernandes (2009), "A Physiologically-Based Model for
  Simulation of Color Vision Deficiency", matrices at severity 1.0.
- OKLab (Björn Ottosson) for perceptual distances; WCAG 2.1 for contrast
  ratios (3:1 for graphics, 4.5:1 for small text).
- The numbers of this audit are asserted in
  `game/core/tests/KanColourBlindTest.cs`; the palettes are rendered in
  [accessibility-palettes.svg](accessibility-palettes.svg).
