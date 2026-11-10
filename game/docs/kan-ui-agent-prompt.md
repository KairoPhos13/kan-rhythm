# Implementation Agent Prompt: KAN Editor UI Polish

> **How to use:** give this document to the agent that will polish the
> **user interface** of the kan! level editor. It is self-contained: what
> exists today, what the owner requires (normative), what must not change,
> and how to verify. Everything referenced here exists in this repository
> today. Functional behavior is **done and working**; this task is purely
> UI/UX, with one look-ahead constraint (the timeline) noted in §7.

---

## Prompt

You are polishing the **UI of the kan! level editor** (kan!forge), the
level-creation pipeline of KAN, a rhythm game for real instruments built
on a fork of **ppy/osu** (osu!lazer, MIT). The owner's words: *"desde un
punto de vista funcional funciona, pero desde el punto de vista de la
interfaz de usuario es horrible"*: functionally it works, visually it is
horrible. Your job is to make it feel **organic, clear and nice** without
changing behavior.

### 0. Ground rules (non-negotiable)

1. **Behavior is frozen.** The role model, the pipeline actions and the
   edit semantics are implemented and tested (29 NUnit tests). Do not
   change `ForgeTrackSet`/`ForgeTrack` semantics, the bridge calls, or the
   job flow; change **how it looks and how it feels**. If a behavior change
   seems necessary, note it in your report instead of doing it.
2. **Zero-warning builds, tests green** (`make test`, 29/29 expected).
   Build everything with `make` in `game/` (defaults are baked in: Release,
   GPU/Vulkan auto, RoFormer runtime ON).
3. **English only** in code, comments, strings and commits (repo rule).
4. **No osu! branding** in what you create (ADR-0011: kan! identity). The
   upstream UI vocabulary (`Form*`, `OsuColour`, fonts) is fine to use;
   that is plumbing, not brand.
5. **Minimize seams**: the standing owner rule (see §2.1). If a change
   makes the editor feel more like *one* program, it is the right change.
6. **Stay on the plugin surface.** All current UI is ruleset-plugin code
   (`Kan.Forge`, compiled against the fork). Avoid fork edits
   (`game/client/`); if one is truly required, it must be logged in
   `game/docs/fork-divergence.md` as a new F-entry.

### 1. Where the UI lives (map)

| File | What it is |
|---|---|
| `game/forge/Editor/KanMediaSection.cs` | the **Resources** section of the editor's setup tab (subclass of the stock `ResourcesSection`): the ONLY file-reading surface: "Import audio tracks (multiple)" navigator, "video provides the audio" toggle, and **"The set"** explorer (compact rows: waveform background + name + role badges + ✕) |
| `game/forge/Editor/KanTracksScreen.cs` | the **"track" tab** (second tab, lowercase; see §2.4): the full track rows (roles, volume/mute, track editor, ✕) and the optional actions (demux / transcribe / render) with progress bars and a status line |
| `game/forge/Editor/WaveformPreview.cs` | shared waveform drawable (96 peak buckets from `TrackAnalysis`) |
| `game/core/Tracks/ForgeTrack.cs` | the track model: roles (music/reference/accompaniment/demux), volume, mute, material class (separated/mixdown), duplicate-import refusal |
| `game/core/Tracks/TrackAnalysis.cs` | waveform data + the "no meaningful audio" rule (peak/RMS below noise floor) |
| `game/forge/Editor/ForgeTrackSession.cs` | the shared track set between the two surfaces |
| fork (do not touch) | `EditorScreenMode.Tracks`, `Ruleset.CreateTracksEditorScreen()` hook (fork-divergence F-11); the stock "Audio Track" row is hidden via a documented reflection call in `KanMediaSection.hideStockAudioSelector()` |

The two surfaces: **Resources imports**; **the track tab selects and
arranges**. Both show the same `ForgeTrackSession.Tracks` set.

### 2. The owner's UX rules (NORMATIVE; these are acceptance criteria)

**2.1 One organic program, minimal seams.**
*"We should merge the existing with the new… minimize the sew points so it
feels organic, and nice."* The creation flow and the stock editor must read
as one continuous tool. No panel glued on top of another; shared visual
language, spacing and rhythm everywhere.

**2.2 One import surface (Resources) and nothing else reads files.**
*"Solo debo importar tracks y video o imagenes en resources… en las otras
partes solo debo poder seleccionar los resources creados o importados."*
Import = Resources only (audio multiple; video and images via the stock
rows). Everywhere else: **selection only**. The stock "Audio Track" selector
must not be a second import; it **connects to the track selected for the
'music' role** (today it is hidden and the music role drives the beatmap
audio; make that connection *visible*, e.g. the row reflects the chosen
track).

**2.3 File navigators, never paths.**
*"At adding the audio track it should show a navigator as currently does
with the background and the video."* Use the same file-chooser pattern as
the stock rows. **No path text boxes anywhere.**

**2.4 The tab is "track", lowercase, second.**
*"Track should be presented as 'track' as all the other tabs start by a
non-capital letter. Also should be the second on the list as it's the first
tool user might want at creating a level."* (Already done: `setup · track ·
compose · …`, keep it.)

**2.5 Show the WHOLE set, with waveforms, and highlight selection.**
*"When selecting the tracks it's not clear enough which tracks have been
selected. I propose to highlight them in the selector explorer, and also to
show the whole set instead of just displaying the last added song and how
many songs have been added (not as text but as a nice list with the
waveform of the track visually represented on the background)."*
- Never "last added + count" text: always the full list.
- **Each row carries its waveform as the visual background** (activity is
  readable before selecting anything: *"a graphic representation of its
  content as a waveform so user might know how is the activity even before
  selecting the track"*).
- **Selected tracks (any role active) must be obviously highlighted.**

**2.6 ✕ to remove; no duplicates.**
*"The list… should allow me to remove the song from the list by showing an
x to remove it. And I should not be able to add a track already added."*
(Implemented: ✕ buttons + central duplicate refusal; polish them, don't
lose them.)

**2.7 "No meaningful audio" marking.**
*"Label, tag, or mark if a track seems to not have meaningful sound and be
placed at the end of the list so it doesn't confuse the user who might think
the track has content, and only should be selected if the user really thinks
that it should be something meaningful in there."* Warning label + demotion
to the end (never removal).

**2.8 Nothing is mandatory.**
*"No debe ser obligatorio demuxear ni transcribir ya que puede ser necesario
en un caso pero no en otro."* Demux and transcription are independent,
optional actions. No forced order, no disabled states implying a required
step, no wizard feel.

**2.9 Track editing creates NEW tracks with a user-chosen name.**
*"Al editar una pista se debe poder crear una nueva que se añada a la lista
con un nombre definido por el usuario."* Non-destructive; the name field is
part of the interaction (today it is a plain text box above three buttons;
make the flow obvious).

**2.10 Video provides the audio (merge), visuals-only optional.**
*"The video and the audio track might be the same… many times the audio will
be included within the video but let's keep the option to import the video
only for the visuals."* Toggle exists; make its effect visible (the
extracted track joins the list as an accompaniment).

### 3. Current UI inventory: what "horrible" means concretely

This is the honest state you are polishing (all working, all ugly):

1. **Everything is a flat stack of stock `Form*` controls.** Track rows are
   ~15 stacked controls tall (roles + volume + mute + the always-expanded
   edit block + three near-identical "create" buttons + ✕). No grouping, no
   hierarchy, no icons, no whitespace rhythm.
2. **The edit block is always expanded** on every row; it should be a
   compact "edit" affordance that unfolds on demand.
3. **Role checkboxes are walls of text** ("reference (the take to
   transcribe)", "accompaniment (mixed into the beatmap audio)"…). Roles
   need scannable chips/badges with icons and color, and the *selected*
   state must read instantly (today: a flat green tint + a text badge line).
4. **The waveform is 96 thin low-alpha bars behind the text**, muddy. It
   needs to be a clear hero element: proper contrast, time axis, and (high
   value) **a visual range selection** instead of typed `from (s)`/`to (s)`
   numbers.
5. **No time context anywhere**: ranges are typed in seconds; no playhead
   link; no duration display per track.
6. **Progress**: two plain bars reused by three actions (render borrows the
   transcription bar); the render has no real progress; status is a single
   bottom line that messages scroll over and get lost. No toasts, no
   history, no spinners for indeterminate work.
7. **Empty states are bare text** ("no tracks yet…"). "analysing…" is a
   placeholder label instead of a skeleton.
8. **The music-role → Audio Track connection is invisible**: the user
   cannot see that the stock row now reflects their choice.
9. **Buttons are generic** and repeated ("create" ×3); no icons; no
   destructive-action styling for ✕/remove; hitbox padding inconsistent.
10. **Fixed heights, ad-hoc margins**, no responsive behavior at smaller
    window sizes; the explorer row is 34 px and clips long names/badges.

### 4. Design direction (suggestions, not limits)

- **Row anatomy (both surfaces):** a hero waveform strip with the track
  name and compact role chips overlaid; selection highlight as a clear
  accent frame/side-bar, not just a tint; ✕ top-right (confirm on
  destructive?); the full control set behind an expand affordance.
- **Roles as chips:** color-coded (`music` · `reference` · `accompaniment`
  · `demux`) with icons; toggling feels instant; exclusivity (music /
  reference are single) reflected in the chips' behavior.
- **Range editing:** drag handles on the waveform for `from`/`to` (the
  number boxes can stay as precise input), with the selected range shaded;
  the three operations become icons on the range (keep / silence / trim).
- **Status → notifications:** use the framework's notification/toast
  vocabulary or a proper status area with history; per-action inline
  progress on the action that runs (demux shows per-track progress;
  *"demux {name}"* already exists in the data).
- **Empty/first-run states:** a short "how this works" hint chain in the
  empty list (import → assign roles → optional actions), icons instead of
  walls of text.
- Keep the osu!framework look-and-feel (`Form*`, `OsuColour`, Torus
  fonts) so the editor stays one program (§2.1).

### 5. Must NOT change (behavioral contract)

- Roles: music (one) / reference (one) / accompaniment (many) / demux
  (queue); demux results rejoin the list and are never demuxable again
  (nor are mixdowns); empty-looking tracks are labelled and sorted last.
- Duplicate imports refused (case-insensitive paths).
- Video-audio merge into the list as an accompaniment; visuals-only switch.
- Actions: demux, transcribe (uses the **reference** automatically), render
  (mixes the audible **accompaniments** into the beatmap audio), all
  optional and independent.
- Track editor: keep range / silence range (timing preserved) / trim → NEW
  named tracks; source never modified.
- The single import surface (Resources) and the hidden stock Audio Track
  row (keep the reflection guard working; improve its *visual*
  integration).
- `make test` stays green (29 tests).

### 6. Acceptance criteria

1. An unfamiliar user can tell **at a glance**: what is in the set, which
   tracks are selected for which role, and what each track contains
   (waveform).
2. The track row fits in roughly a third of today's vertical space in its
   collapsed state; expanding is optional and obvious.
3. Role selection reads instantly (chips + highlight); no wall-of-text
   captions needed to understand state.
4. Range editing can be done visually on the waveform (typed input remains
   available).
5. Every action reports its own progress in place; messages appear as
   notifications and are not lost.
6. The whole flow still obeys §2.1-2.10: spot-check each rule.
7. `make` builds clean (0 warnings); `make test` = 29/29; strings English;
   no osu! branding added.

### 7. Look-ahead (do not build, but do not paint us into a corner)

The next functional milestone is the **timeline with temporal segments**
(the owner: *"separar temporalmente segmentos de las pistas en el editor es
un must"*; overlapping-range cases need positioned segments: split/move per
time on any track, plus the video track and its trim use case). Keep the
waveforms, row anatomy and selection vocabulary ready to grow a time axis
and per-segment objects: e.g. prefer designs where the waveform strip can
become a timeline cell.

### 8. References

- `game/docs/editor-pipeline.md`: pipeline architecture, role model,
  error states, dispatch threading (§6b is the UI contract).
- `game/docs/instrument-modes.md`: instrument/roles product model.
- `docs/adr/0011-rebrand-kan-identity.md`: kan! identity rules.
- `docs/implementation/kan-editor-agent-prompt.md`: the functional spec
  this UI serves (do not re-implement its scope).
