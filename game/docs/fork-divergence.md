# game/client — fork divergence log

Every change made **inside the fork** (`game/client/` = submodule of
`github.com/KairoPhos13/kan`, itself a fork of `ppy/osu`) is logged here:
**what / why / merge-conflict cost / upstream-PR status**. This file is the
compliance record required by [ADR-0010](../adr/0010-osu-lazer-base-client.md)
and [osu-integration.md §4/§6](../architecture/osu-integration.md).

Policy (rebase-ability first, ADR-0011 §4):

- Adapted upstream files keep `osu.Game.*` / `osu.Framework.*` namespaces and
  their original copyright headers. Each edited file carries a
  `kan! rebrand (ADR-0011)` marker comment at the delta.
- New kan! code uses `Kan.*` namespaces and lives in **new files** wherever
  possible (zero-conflict cost).
- "Substantially modified" upstream files are marked with the same comment
  marker; so far all deltas are small and mechanical (see below), so no file
  qualifies as substantially modified.

## Pin log

| Date | Old pin | New pin | Why | Build |
|---|---|---|---|---|
| 2026-10-12 | *(none — first wiring)* | `816df139fd` (upstream `ppy/osu` master) | fork created + submodule wired (previous session) | green |
| 2026-10-12 | `816df139fd` | `2970c68` (branch `agent/game` on `github.com/KairoPhos13/kan`) | Phase 0 rebrand baseline (entries F-1…F-6) | green (0 warnings / 0 errors, verified on Linux) |
| 2026-10-13 | `2970c68` | `e704f03` (branch `agent/game`) | owner direction: stock game modes + osu!stable legacy IPC removed (entries F-9/F-10) | green (0 warnings / 0 errors; run verified with only the kan! ruleset) |
| 2026-10-13 | `e704f03` | `ec02860` (branch `agent/game`) | ruleset-provided "tracks" editor tab (entry F-11) | green (0 warnings / 0 errors; tab verified live) |
| 2026-10-13 | `ec02860` | `e0c1cc9` (branch `agent/game`) | F-11 follow-up: tab presented as "track", second in the list | green (0 warnings / 0 errors) |
| 2026-10-15 | `e0c1cc9` | `ca49bab` (branch `agent/game`) | `110dedc` F-12 summary timeline, `391afc1` F-11 follow-up (timing before compose), `b33c540` F-13 compose screen, `ca49bab` F-14 hit-object-less gameplay clock | green (0 warnings / 0 errors; editor and gameplay verified) |
| 2026-10-15 | `ca49bab` | `cc2ccac` (branch `agent/game`) | F-15 design screen | green (0 warnings / 0 errors; design tab verified in an isolated instance) |
| 2026-10-16 | `cc2ccac` | `9e92011` (branch `agent/game`) | F-16 player loader settings | green (0 warnings / 0 errors; panel verified in an isolated instance) |
| 2026-10-19 | `9e92011` | `e4f19a2` (branch `agent/game`) | F-17 ruleset-chosen result time | green (0 warnings / 0 errors; hit error meter verified in an isolated instance) |
| 2026-10-23 | `e4f19a2` | `400efe6` (branch `agent/game`) | F-18 patched MessagePack and SignalR packages | green (0 warnings / 0 errors; gameplay verified in an isolated instance) |
| 2026-10-27 | `400efe6` | `9a6beeb` (branch `agent/game`) | F-19 ruleset-filtered general verify checks | green (0 warnings / 0 errors; verify tab verified in an isolated instance) |

> **⚠ Push blocker (2026-10-13):** the build machine has **no GitHub
> credentials** (SSH keys rejected, no HTTPS token), so commits `0f5db37`,
> `2970c68`, `e704f03`, `ec02860` and `e0c1cc9` exist only in the local
> submodule object store. **Before merging `agent/game` into `main`, the
> maintainer must push them:**
> `git -C game/client push origin e0c1cc9:refs/heads/agent/game`
> (then merge/fast-forward the fork's `master` as desired). Until then,
> `git submodule update` works on this machine's shared object store only.

## Entries

### F-1 — `osu.Game/OsuGameBase.cs`: `GAME_NAME` → `kan!`

- **What:** `GAME_NAME` constants ("osu!" / "osu! (development)") become
  "kan!" / "kan! (development)". One-line delta + marker comment.
- **Why:** window title, window "friendly name" (Velopack), and every UI
  place derived from `Name` must say kan! (ADR-0011 §1; mission deliverable
  "window title says kan!"). Verified live: `xdotool` reports window title
  `kan!`.
- **Merge-conflict cost:** low (one line, rarely touched upstream).
- **Upstream-PR:** none (product-specific string; upstream will never want
  this).

### F-2 — `osu.Desktop/osu.Desktop.csproj`: assembly/product identity → `kan`

- **What:** `AssemblyName` `osu!` → `kan`, `Title`/`AssemblyTitle`/`Product`
  → `kan!`, `Description` → `kan! — grab your instrument. You kan!`.
- **Why:** ADR-0011 §4: "assembly names and package IDs for *our* artifacts
  say kan; upstream assembly references keep their names". The executable is
  our artifact (verified: build output `kan.dll` + `kan` apphost).
- **Merge-conflict cost:** low-medium (csproj property block; upstream
  occasionally edits `Description`/`Version` tooling lines).
- **Upstream-PR:** none.

### F-3 — `osu.Desktop/Program.cs`: `base_game_name` → `kan`

- **What:** storage/user-data root name `osu`/`osu-development` →
  `kan`/`kan-development`. The osu!**stable** import paths
  (`getStableInstallPathFromRegistry("osu!")`, `LocalApplicationData\osu!`)
  are intentionally unchanged — they locate the user's osu! install.
- **Why:** standalone product identity: kan! keeps its own data folder
  (verified live: `~/.local/share/kan/`). Also avoids colliding with a user's
  osu!lazer data.
- **Merge-conflict cost:** low (constants; upstream touches them rarely).
- **Upstream-PR:** none.

### F-4 — `osu.Game/Screens/Menu/IntroSequence.cs`: splash text → `kan!`

- **What:** intro/splash `welcomeText` "welcome" → "kan!" (one line).
- **Why:** deliverable "splash says kan!". The intro's *visuals* (rings,
  osu! logo texture) are upstream assets and part of the identity-design
  work (game/TODO.md "Identity design v0") — not touched here.
- **Merge-conflict cost:** low.
- **Upstream-PR:** none.

### F-5 — about screen: `KanAboutSettings` + `GeneralSection` hook

- **What:** new file
  `osu.Game/Overlays/Settings/Sections/General/KanAboutSettings.cs`
  (namespace `Kan.Client`) + one `Add(new Kan.Client.KanAboutSettings());`
  line in `osu.Game/Overlays/Settings/Sections/GeneralSection.cs`.
  Shows "kan!", "Grab your instrument. You kan!", the credits line
  "Built on osu!lazer (MIT) — ppy/osu contributors", and the embedded
  `THIRD_PARTY_NOTICES.md`.
- **Why:** deliverable "about screen says kan!; credits name ppy/osu
  contributors"; osu-integration.md §4 requires the notices to be shown
  in-client. Settings → General is the most discoverable hook point that
  does not require invasive UI changes.
- **Merge-conflict cost:** minimal — the hook is one line in a list where
  upstream rarely conflicts; the rest is a new file.
- **Upstream-PR:** none (though a generic "about screen" concept could be
  proposed upstream someday — not a fix).

### F-6 — `osu.Game/osu.Game.csproj`: embed `THIRD_PARTY_NOTICES.md`

- **What:** `<EmbeddedResource Include="..\THIRD_PARTY_NOTICES.md"
  LogicalName="THIRD_PARTY_NOTICES.md"/>` (one ItemGroup).
- **Why:** the about screen (F-5) must ship the notices in-binary.
- **Merge-conflict cost:** minimal (own labelled ItemGroup).
- **Upstream-PR:** none.

### F-7 — `THIRD_PARTY_NOTICES.md` at the fork root (generated artifact)

- **What:** generated by `game/tools/gen-third-party-notices.py` (monorepo);
  lists the licence identity of the fork + all ~140 NuGet packages in the
  `osu.Desktop` closure + the full upstream MIT text + BASS terms pointer.
- **Why:** license compliance (ADR-0010 §5, CONTRIBUTING §6).
- **Merge-conflict cost:** none (new file, regenerated — never merged with
  upstream).
- **Upstream-PR:** none.

### F-8 — ruleset plugin naming constraint (no fork change, recorded for context)

- **What:** the kan ruleset assembly is named `osu.Game.Rulesets.Kan.dll`
  while its namespaces are `Kan.*` (`game/ruleset/`).
- **Why:** the upstream ruleset loader only discovers DLLs matching
  `osu.Game.Rulesets.*.dll` (`RulesetStore.loadFromDisk`/`loadUserRulesets`).
  This is the single place where the upstream prefix must survive in kan!
  artifacts; everything else follows ADR-0011 `Kan.*`.
- **Merge-conflict cost:** none (no fork file touched).
- **Upstream-PR:** none (could be a nice upstream feature: accept arbitrary
  ruleset assembly names).

### F-9 — stock game modes removed from the desktop client

- **What:** dropped the four `osu.Game.Rulesets.{Osu,Catch,Mania,Taiko}`
  project references from `osu.Desktop/osu.Desktop.csproj`. The projects
  stay in the fork repository (tests/tooling still use them); they are just
  not built into or shipped with the desktop client. After this change the
  client output contains only `osu.Game.Rulesets.Kan.dll` (note: stale
  ruleset DLLs linger in an existing build output folder — `dotnet clean` or
  delete them once).
- **Why:** owner direction 2026-10-12: kan! has **no osu! game modes** —
  the mode selector belongs to **instruments** (see the monorepo's
  `game/docs/instrument-modes.md`). Also aligns with ADR-0011 ("not a
  mod"): presenting osu!standard/taiko/catch/mania as playable modes was
  pure upstream residue. `osu.Game` itself has zero compile-time
  dependencies on the stock rulesets (verified before the change).
- **Merge-conflict cost:** low (one ItemGroup in the desktop csproj;
  upstream occasionally touches these lines when adding rulesets).
- **Upstream-PR:** none (product-scope decision).

### F-10 — osu!stable legacy IPC removed

- **What:** deleted `osu.Desktop/LegacyIpc/` (4 files) and its wiring in
  `osu.Desktop/Program.cs`.
- **Why:** it exists solely to serve osu!stable clients (legacy difficulty
  calculation over TCP port 45357) and hardcodes the stock legacy ruleset
  IDs 0–3 (`OsuRuleset`/`TaikoRuleset`/`CatchRuleset`/`ManiaRuleset`) — the
  only compile-time dependency of the desktop client on the stock modes.
  kan! has no osu!stable legacy. Removed together with F-9 (owner direction
  2026-10-12).
- **Merge-conflict cost:** low-medium (deleting upstream files; these files
  rarely change upstream — rebase conflict only if upstream edits them).
- **Upstream-PR:** none (this is upstream functionality others may want; we
  simply do not ship it).

### F-11 — ruleset-provided "tracks" editor tab

- **What:** `EditorScreenMode.Tracks` + `Ruleset.CreateTracksEditorScreen()`
  virtual (returns null → the tab is hidden) + `Editor`: tab visibility
  filtered by the hook, screen instance created once and reused. The screen
  implementation lives in kan code (`Kan.Forge.Editor.KanTracksScreen`).
- **Why:** owner direction 2026-10-13 — the track editor (unified resource
  list, roles, non-destructive track editing, actions) is a first-class
  editor tab next to setup/compose/design/timing/verify. ADR-0010 §2
  explicitly allows first-class kan!forge screens; the hook itself is
  generic and upstream-shaped.
- **Follow-up (owner 2026-10-14):** `Timing` moved before `Compose` in
  the enum, which orders the tabs: setup, track, timing, compose, design,
  verify. Aligning the beat grid to each section first saves problems
  when composing. Mode values are only kept in memory (`EditorState`), and
  shortcuts select tabs by name, so nothing stored depends on the order.
- **Merge-conflict cost:** low — one enum member (plus the moved `Timing`
  member), one virtual, two small touch points in `Editor.cs`.
- **Upstream-PR:** candidate ("ruleset-provided editor screens") once the
  shape settles; not yet offered.

### F-12 — editor summary timeline follows the editor clock's length

- **What:** `TimelinePart.updateRelativeChildSize()` (bottom-bar summary
  timeline: progress marker, breaks, bookmarks, kiai, control points,
  preview time) reads `EditorClock.TrackLength` instead of
  `beatmap.Value.Track.Length`. One line, plus a comment.
- **Why:** owner direction 2026-10-14. While the track tab is open, the
  editor clock runs on a silent track spanning the whole set
  (`EditorClock.ChangeSource`, public API). The time display and seeking
  already follow the clock, but the summary timeline kept the beatmap
  track's scale: the marker ran out after 1 s while playback continued. For
  every stock tab the value is identical, because the clock's source *is* the
  beatmap track, with the same 60 s fallback while loading.
- **Merge-conflict cost:** very low — one line in one method.
- **Upstream-PR:** candidate (consistency fix: the summary timeline should
  scale with the clock it displays); not yet offered.

### F-13 — ruleset-provided "compose" editor screen

- **What:** `Ruleset.CreateComposeEditorScreen()` virtual (returns null →
  the stock `ComposeScreen`); `Editor`: the compose case asks the ruleset
  first, and the timestamp-selection hand-off only queries a
  `HitObjectComposer` when the stock `ComposeScreen` is showing.
- **Why:** owner direction 2026-10-14 — kan!'s compose tab is the
  transcription/score editor (piano roll: notes with pitch, duration,
  intensity and style, spectrogram, mic section recording). osu!'s
  `HitObjectComposer` is built around placing objects on a playfield; the
  kan! screen lives in plugin code (`Kan.Forge.Editor.KanComposeScreen`),
  like the track tab (F-11).
- **Merge-conflict cost:** low — one virtual, one line in the mode switch,
  one guard.
- **Upstream-PR:** candidate together with F-11 ("ruleset-provided editor
  screens").

### F-14 — gameplay clock with a hit-object-less source beatmap

- **What:** `MasterGameplayClockContainer.findEarliestStartTime` reads the
  first hit object's time with `FirstOrDefault()` (0 when there is none)
  instead of `First()`.
- **Why:** gameplay MVP (owner 2026-10-14). A kan! level keeps its notes in
  its score file (`kan.score.json`); the ruleset's converter turns them into
  hit objects, but the *source* beatmap that this method reads may have
  none, which threw "Sequence contains no elements" on entering the level.
- **Merge-conflict cost:** trivial — one line.
- **Upstream-PR:** candidate (harmless robustness fix).

### F-15 — ruleset-provided "design" editor screen

- **What:** `Ruleset.CreateDesignEditorScreen()` virtual (returns null →
  the stock `DesignScreen`); `Editor`: the design case asks the ruleset
  first.
- **Why:** owner direction 2026-10-15 — kan!'s design tab is the level's
  look and rewards (sections, rests, climaxes, palette, effect strength),
  saved as `kan.design.json` in the level. osu!'s design screen is a
  storyboard view. The screen lives in plugin code
  (`Kan.Forge.Editor.Design.KanDesignScreen`), like F-11 and F-13.
- **Merge-conflict cost:** low — one virtual, one line in the mode switch.
- **Upstream-PR:** candidate together with F-11/F-13 ("ruleset-provided
  editor screens").

### F-16 — ruleset-provided player loader settings

- **What:** `Ruleset.CreatePlayerLoaderSettings()` virtual (empty by
  default); `PlayerLoader` appends its groups after the stock visual /
  audio / input groups of the side panel.
- **Why:** owner direction 2026-10-15 — the notation (note names as
  letters or Do-Re-Mi, a colour per note, note size) is chosen right
  before playing, in the panel that opens when a level is picked. The
  group lives in plugin code (`Kan.Ruleset.UI.KanNotationSettings`).
- **Merge-conflict cost:** low — one virtual, one expression in
  `PlayerLoader`'s panel.
- **Upstream-PR:** candidate (rulesets may want per-play settings there).

### F-17 — ruleset-chosen result time

- **What:** `DrawableHitObject.ResultTime` virtual (default
  `Time.Current`); `ApplyResult` stamps `Result.RawTime` with it.
- **Why:** owner report 2026-10-17 — the hit error meter (tortoise / hare)
  always said "late". kan! decides a note when its pitch arrives, after
  the attack, so stamping "now" added the detection wait (+50 ms or more)
  to every hit, in the meters and the results' timing graph.
  `DrawableKanHitObject` stamps a hit at the note's start + the judge's
  offset (the attack). Accuracy and score were not affected (they go by
  the result type).
- **Merge-conflict cost:** low — one virtual property and one changed
  assignment in `ApplyResult`.
- **Upstream-PR:** candidate (any ruleset that decides after the input).

### F-18 — patched MessagePack and SignalR packages

- **What:** `osu.Game/osu.Game.csproj` — `MessagePack` 3.1.8 → 3.1.12;
  `Microsoft.AspNetCore.SignalR.Client`, `.Protocols.MessagePack`,
  `.Protocols.NewtonsoftJson` 10.0.12 → 10.0.13 (patch releases).
- **Why:** owner report 2026-10-23 — every build printed NuGet security
  advisories: GHSA-qhrr-8q5h-9q3h (MessagePack, moderate; fixed in 3.1.9),
  GHSA-86xp-94pg-8r6r and GHSA-334f-vvp4-7g6p (SignalR, moderate and high;
  fixed in 10.0.13). The build is now free of them. Left as upstream holds
  them, silenced on purpose: AutoMapper 13.0.1 (its fix is 15.1.1, past
  its licence change) and SixLabors.ImageSharp 3.1.11 (its fixes are in
  4.1.2, a major version).
- **Merge-conflict cost:** low — version numbers; take upstream's when it
  has moved past them.
- **Upstream-PR:** not needed (upstream bumps these routinely).

### F-19 — a ruleset leaves out general verifier checks

- **What:** `Ruleset.AppliesGeneralCheck(ICheck)` virtual (default true);
  `Screens/Edit/Verify/IssueList.cs` drops the general checks' issues the
  ruleset says do not apply.
- **Why:** owner 2026-10-25 — the verify tab reported "less than 30 seconds
  of drain time, currently 0" and "100% unused audio at the end" for every
  kan! level: its notes live in the score, not in the beatmap's hit
  objects. kan! turns those checks off and runs its own
  (`KanBeatmapVerifier`, editor-pipeline.md §6h).
- **Merge-conflict cost:** low — one virtual method and a filter in
  `IssueList.Refresh`.
- **Upstream-PR:** candidate (any ruleset with objects outside the beatmap).

## String sweep — remaining scope (deferred, game/TODO.md)

Identified user-facing `osu!`/osu references still present in adapted code
(mechanical deltas to be logged here when done):

- `osu.Desktop/Program.cs` — SDL message-box text ("run osu!"), multi-instance
  log line
- `osu.Desktop/NVAPI.cs` — NVIDIA profile names
- `osu.Desktop/Windows/WindowsAssociationManager.cs` +
  `osu.Game/Localisation/WindowsAssociationManagerStrings.cs` — file/protocol
  associations (`osu://`, `osump://` protocol scheme decision needed)
- `osu.Game/Screens/Edit/Components/Menus/EditorMenuBar.cs` — editor title
  text ("osu!editor")
- `osu.Game/Online/API/OsuWebRequest.cs` / `OsuJsonWebRequest.cs` —
  `UserAgent` strings
- `osu.Game/OsuGameBase.cs` — `OSU_PROTOCOL = "osu://"` (product decision:
  keep for compatibility or move to `kan://`)
- Online endpoints still point at osu! production servers (API, news,
  changelog) — needs its own work item (server/ OpenAPI), not just strings
- Default skin/logo/triangle assets and the `lazer.ico` app icon (identity
  design v0 — trademark hygiene ADR-0011 §5)
