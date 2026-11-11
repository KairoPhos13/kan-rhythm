# game/client: fork divergence log

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
| 2026-10-12 | *(none, first wiring)* | `816df139fd` (upstream `ppy/osu` master) | fork created + submodule wired (previous session) | green |
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
| 2026-10-27 | `9a6beeb` | `5e3d1b2` (branch `agent/game`) | F-20 offline until kan!'s servers, F-21 brand resources, string sweep first pass | green (0 warnings / 0 errors; no ppy.sh request and a brand logo in an isolated instance) |
| 2026-11-09 | `5e3d1b2` | `2c3660d` (branch `agent/game`) | F-22 no osu! voice or theme music, gold logo disc, provisional icons | green (0 warnings / 0 errors; isolated run: kan! intro outline, gold menu logo, kan! backgrounds) |
| 2026-11-09 | `2c3660d` | `2c3660d` (unchanged; branch `agent/ci` holds no fork commit) | F-23 platform builds: Windows/macOS packaging lives outside the fork (`game/packaging/`, CI), no fork change | n/a, pin unchanged; Linux build verified green (see F-23) |
| 2026-11-09 | `2c3660d` | `e32d2a3` (branch `agent/game`) | F-24 kan!'s own server, never osu!'s | green (0 warnings / 0 errors; isolated run logged in against the local kan! server, no ppy.sh request) |
| 2026-11-09 | `e32d2a3` | `e012336` (branch `agent/game`) | F-24 updater reads kan-rhythm releases | green (0 warnings / 0 errors) |
| 2026-11-09 | `e012336` | `a132ea8183` (branch `agent/game`) | Repository home is the KairoPhos13 account | green (strings only) |
| 2026-11-09 | `daeac53` | `4fa5313` (branch `main`) | `0fdaf70` clause-separating dashes removed from kan!-added text; `4fa5313` the osu! README replaced with kan!'s own (F-25) | green (0 warnings / 0 errors; make check green, comments/notices/UI strings only) |
| 2026-11-10 | `4fa5313` | `bb17154f0b` (branch `main`) | F-24 server is `kan-api.kairophos.link`; F-26 account creation without osu!'s warning and email promises | green (0 warnings / 0 errors) |
| 2026-11-10 | `bb17154f0b` | `ef737e8068` (branch `agent/quiet-online`) | F-27 quiet connectors when realtime services do not exist | green (0 warnings / 0 errors) |
| 2026-11-10 | `ef737e8068` | `435757ed2a` (branch `main`) | F-28 osu!stable import and osu!-only links hidden from the UI (issue 24) | green (0 warnings / 0 errors; make check green) |
| 2026-11-10 | `435757ed2a` | `e000468941` (branch `main`) | String sweep, second pass: English strings that mean kan! itself (issue 22) | green (0 warnings / 0 errors; `make check` green) |

> **⚠ Push blocker (2026-10-13):** the build machine has **no GitHub
> credentials** (SSH keys rejected, no HTTPS token), so commits `0f5db37`,
> `2970c68`, `e704f03`, `ec02860` and `e0c1cc9` exist only in the local
> submodule object store. **Before merging `agent/game` into `main`, the
> maintainer must push them:**
> `git -C game/client push origin e0c1cc9:refs/heads/agent/game`
> (then merge/fast-forward the fork's `master` as desired). Until then,
> `git submodule update` works on this machine's shared object store only.

## Entries

### F-1 (`osu.Game/OsuGameBase.cs`): `GAME_NAME` → `kan!`

- **What:** `GAME_NAME` constants ("osu!" / "osu! (development)") become
  "kan!" / "kan! (development)". One-line delta + marker comment.
- **Why:** window title, window "friendly name" (Velopack), and every UI
  place derived from `Name` must say kan! (ADR-0011 §1; mission deliverable
  "window title says kan!"). Verified live: `xdotool` reports window title
  `kan!`.
- **Merge-conflict cost:** low (one line, rarely touched upstream).
- **Upstream-PR:** none (product-specific string; upstream will never want
  this).

### F-2 (`osu.Desktop/osu.Desktop.csproj`): assembly/product identity → `kan`

- **What:** `AssemblyName` `osu!` → `kan`, `Title`/`AssemblyTitle`/`Product`
  → `kan!`, `Description` → `kan!: grab your instrument. You kan!`.
- **Why:** ADR-0011 §4: "assembly names and package IDs for *our* artifacts
  say kan; upstream assembly references keep their names". The executable is
  our artifact (verified: build output `kan.dll` + `kan` apphost).
- **Merge-conflict cost:** low-medium (csproj property block; upstream
  occasionally edits `Description`/`Version` tooling lines).
- **Upstream-PR:** none.

### F-3 (`osu.Desktop/Program.cs`): `base_game_name` → `kan`

- **What:** storage/user-data root name `osu`/`osu-development` →
  `kan`/`kan-development`. The osu!**stable** import paths
  (`getStableInstallPathFromRegistry("osu!")`, `LocalApplicationData\osu!`)
  are intentionally unchanged; they locate the user's osu! install.
- **Why:** standalone product identity: kan! keeps its own data folder
  (verified live: `~/.local/share/kan/`). Also avoids colliding with a user's
  osu!lazer data.
- **Merge-conflict cost:** low (constants; upstream touches them rarely).
- **Upstream-PR:** none.

### F-4 (`osu.Game/Screens/Menu/IntroSequence.cs`): splash text → `kan!`

- **What:** intro/splash `welcomeText` "welcome" → "kan!" (one line).
- **Why:** deliverable "splash says kan!". The intro's *visuals* (rings,
  osu! logo texture) are upstream assets and part of the identity-design
  work (game/TODO.md "Identity design v0"), not touched here.
- **Merge-conflict cost:** low.
- **Upstream-PR:** none.

### F-5 (about screen): `KanAboutSettings` + `GeneralSection` hook

- **What:** new file
  `osu.Game/Overlays/Settings/Sections/General/KanAboutSettings.cs`
  (namespace `Kan.Client`) + one `Add(new Kan.Client.KanAboutSettings());`
  line in `osu.Game/Overlays/Settings/Sections/GeneralSection.cs`.
  Shows "kan!", "Grab your instrument. You kan!", the credits line
  "Built on osu!lazer (MIT), ppy/osu contributors", and the embedded
  `THIRD_PARTY_NOTICES.md`.
- **Why:** deliverable "about screen says kan!; credits name ppy/osu
  contributors"; osu-integration.md §4 requires the notices to be shown
  in-client. Settings → General is the most discoverable hook point that
  does not require invasive UI changes.
- **Merge-conflict cost:** minimal; the hook is one line in a list where
  upstream rarely conflicts; the rest is a new file.
- **Upstream-PR:** none (though a generic "about screen" concept could be
  proposed upstream someday, not a fix).

### F-6 (`osu.Game/osu.Game.csproj`): embed `THIRD_PARTY_NOTICES.md`

- **What:** `<EmbeddedResource Include="..\THIRD_PARTY_NOTICES.md"
  LogicalName="THIRD_PARTY_NOTICES.md"/>` (one ItemGroup).
- **Why:** the about screen (F-5) must ship the notices in-binary.
- **Merge-conflict cost:** minimal (own labelled ItemGroup).
- **Upstream-PR:** none.

### F-7: `THIRD_PARTY_NOTICES.md` at the fork root (generated artifact)

- **What:** generated by `game/tools/gen-third-party-notices.py` (monorepo);
  lists the licence identity of the fork + all ~140 NuGet packages in the
  `osu.Desktop` closure + the full upstream MIT text + BASS terms pointer.
- **Why:** license compliance (ADR-0010 §5, CONTRIBUTING §6).
- **Merge-conflict cost:** none (new file, regenerated; never merged with
  upstream).
- **Upstream-PR:** none.

### F-8: ruleset plugin naming constraint (no fork change, recorded for context)

- **What:** the kan ruleset assembly is named `osu.Game.Rulesets.Kan.dll`
  while its namespaces are `Kan.*` (`game/ruleset/`).
- **Why:** the upstream ruleset loader only discovers DLLs matching
  `osu.Game.Rulesets.*.dll` (`RulesetStore.loadFromDisk`/`loadUserRulesets`).
  This is the single place where the upstream prefix must survive in kan!
  artifacts; everything else follows ADR-0011 `Kan.*`.
- **Merge-conflict cost:** none (no fork file touched).
- **Upstream-PR:** none (could be a nice upstream feature: accept arbitrary
  ruleset assembly names).

### F-9: stock game modes removed from the desktop client

- **What:** dropped the four `osu.Game.Rulesets.{Osu,Catch,Mania,Taiko}`
  project references from `osu.Desktop/osu.Desktop.csproj`. The projects
  stay in the fork repository (tests/tooling still use them); they are just
  not built into or shipped with the desktop client. After this change the
  client output contains only `osu.Game.Rulesets.Kan.dll` (note: stale
  ruleset DLLs linger in an existing build output folder: `dotnet clean` or
  delete them once).
- **Why:** owner direction 2026-10-12: kan! has **no osu! game modes**;
  the mode selector belongs to **instruments** (see the monorepo's
  `game/docs/instrument-modes.md`). Also aligns with ADR-0011 ("not a
  mod"): presenting osu!standard/taiko/catch/mania as playable modes was
  pure upstream residue. `osu.Game` itself has zero compile-time
  dependencies on the stock rulesets (verified before the change).
- **Merge-conflict cost:** low (one ItemGroup in the desktop csproj;
  upstream occasionally touches these lines when adding rulesets).
- **Upstream-PR:** none (product-scope decision).

### F-10: osu!stable legacy IPC removed

- **What:** deleted `osu.Desktop/LegacyIpc/` (4 files) and its wiring in
  `osu.Desktop/Program.cs`.
- **Why:** it exists solely to serve osu!stable clients (legacy difficulty
  calculation over TCP port 45357) and hardcodes the stock legacy ruleset
  IDs 0-3 (`OsuRuleset`/`TaikoRuleset`/`CatchRuleset`/`ManiaRuleset`), the
  only compile-time dependency of the desktop client on the stock modes.
  kan! has no osu!stable legacy. Removed together with F-9 (owner direction
  2026-10-12).
- **Merge-conflict cost:** low-medium (deleting upstream files; these files
  rarely change upstream; rebase conflict only if upstream edits them).
- **Upstream-PR:** none (this is upstream functionality others may want; we
  simply do not ship it).

### F-11: ruleset-provided "tracks" editor tab

- **What:** `EditorScreenMode.Tracks` + `Ruleset.CreateTracksEditorScreen()`
  virtual (returns null → the tab is hidden) + `Editor`: tab visibility
  filtered by the hook, screen instance created once and reused. The screen
  implementation lives in kan code (`Kan.Forge.Editor.KanTracksScreen`).
- **Why:** owner direction 2026-10-13: the track editor (unified resource
  list, roles, non-destructive track editing, actions) is a first-class
  editor tab next to setup/compose/design/timing/verify. ADR-0010 §2
  explicitly allows first-class kan!forge screens; the hook itself is
  generic and upstream-shaped.
- **Follow-up (owner 2026-10-14):** `Timing` moved before `Compose` in
  the enum, which orders the tabs: setup, track, timing, compose, design,
  verify. Aligning the beat grid to each section first saves problems
  when composing. Mode values are only kept in memory (`EditorState`), and
  shortcuts select tabs by name, so nothing stored depends on the order.
- **Merge-conflict cost:** low; one enum member (plus the moved `Timing`
  member), one virtual, two small touch points in `Editor.cs`.
- **Upstream-PR:** candidate ("ruleset-provided editor screens") once the
  shape settles; not yet offered.

### F-12: editor summary timeline follows the editor clock's length

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
- **Merge-conflict cost:** very low; one line in one method.
- **Upstream-PR:** candidate (consistency fix: the summary timeline should
  scale with the clock it displays); not yet offered.

### F-13: ruleset-provided "compose" editor screen

- **What:** `Ruleset.CreateComposeEditorScreen()` virtual (returns null →
  the stock `ComposeScreen`); `Editor`: the compose case asks the ruleset
  first, and the timestamp-selection hand-off only queries a
  `HitObjectComposer` when the stock `ComposeScreen` is showing.
- **Why:** owner direction 2026-10-14: kan!'s compose tab is the
  transcription/score editor (piano roll: notes with pitch, duration,
  intensity and style, spectrogram, mic section recording). osu!'s
  `HitObjectComposer` is built around placing objects on a playfield; the
  kan! screen lives in plugin code (`Kan.Forge.Editor.KanComposeScreen`),
  like the track tab (F-11).
- **Merge-conflict cost:** low; one virtual, one line in the mode switch,
  one guard.
- **Upstream-PR:** candidate together with F-11 ("ruleset-provided editor
  screens").

### F-14: gameplay clock with a hit-object-less source beatmap

- **What:** `MasterGameplayClockContainer.findEarliestStartTime` reads the
  first hit object's time with `FirstOrDefault()` (0 when there is none)
  instead of `First()`.
- **Why:** gameplay MVP (owner 2026-10-14). A kan! level keeps its notes in
  its score file (`kan.score.json`); the ruleset's converter turns them into
  hit objects, but the *source* beatmap that this method reads may have
  none, which threw "Sequence contains no elements" on entering the level.
- **Merge-conflict cost:** trivial; one line.
- **Upstream-PR:** candidate (harmless robustness fix).

### F-15: ruleset-provided "design" editor screen

- **What:** `Ruleset.CreateDesignEditorScreen()` virtual (returns null →
  the stock `DesignScreen`); `Editor`: the design case asks the ruleset
  first.
- **Why:** owner direction 2026-10-15: kan!'s design tab is the level's
  look and rewards (sections, rests, climaxes, palette, effect strength),
  saved as `kan.design.json` in the level. osu!'s design screen is a
  storyboard view. The screen lives in plugin code
  (`Kan.Forge.Editor.Design.KanDesignScreen`), like F-11 and F-13.
- **Merge-conflict cost:** low; one virtual, one line in the mode switch.
- **Upstream-PR:** candidate together with F-11/F-13 ("ruleset-provided
  editor screens").

### F-16: ruleset-provided player loader settings

- **What:** `Ruleset.CreatePlayerLoaderSettings()` virtual (empty by
  default); `PlayerLoader` appends its groups after the stock visual /
  audio / input groups of the side panel.
- **Why:** owner direction 2026-10-15: the notation (note names as
  letters or Do-Re-Mi, a colour per note, note size) is chosen right
  before playing, in the panel that opens when a level is picked. The
  group lives in plugin code (`Kan.Ruleset.UI.KanNotationSettings`).
- **Merge-conflict cost:** low; one virtual, one expression in
  `PlayerLoader`'s panel.
- **Upstream-PR:** candidate (rulesets may want per-play settings there).

### F-17: ruleset-chosen result time

- **What:** `DrawableHitObject.ResultTime` virtual (default
  `Time.Current`); `ApplyResult` stamps `Result.RawTime` with it.
- **Why:** owner report 2026-10-17: the hit error meter (tortoise / hare)
  always said "late". kan! decides a note when its pitch arrives, after
  the attack, so stamping "now" added the detection wait (+50 ms or more)
  to every hit, in the meters and the results' timing graph.
  `DrawableKanHitObject` stamps a hit at the note's start + the judge's
  offset (the attack). Accuracy and score were not affected (they go by
  the result type).
- **Merge-conflict cost:** low; one virtual property and one changed
  assignment in `ApplyResult`.
- **Upstream-PR:** candidate (any ruleset that decides after the input).

### F-18: patched MessagePack and SignalR packages

- **What:** `osu.Game/osu.Game.csproj`: `MessagePack` 3.1.8 → 3.1.12;
  `Microsoft.AspNetCore.SignalR.Client`, `.Protocols.MessagePack`,
  `.Protocols.NewtonsoftJson` 10.0.12 → 10.0.13 (patch releases).
- **Why:** owner report 2026-10-23: every build printed NuGet security
  advisories: GHSA-qhrr-8q5h-9q3h (MessagePack, moderate; fixed in 3.1.9),
  GHSA-86xp-94pg-8r6r and GHSA-334f-vvp4-7g6p (SignalR, moderate and high;
  fixed in 10.0.13). The build is now free of them. Left as upstream holds
  them, silenced on purpose: AutoMapper 13.0.1 (its fix is 15.1.1, past
  its licence change) and SixLabors.ImageSharp 3.1.11 (its fixes are in
  4.1.2, a major version).
- **Merge-conflict cost:** low; version numbers; take upstream's when it
  has moved past them.
- **Upstream-PR:** not needed (upstream bumps these routinely).

### F-19: a ruleset leaves out general verifier checks

- **What:** `Ruleset.AppliesGeneralCheck(ICheck)` virtual (default true);
  `Screens/Edit/Verify/IssueList.cs` drops the general checks' issues the
  ruleset says do not apply.
- **Why:** owner 2026-10-25: the verify tab reported "less than 30 seconds
  of drain time, currently 0" and "100% unused audio at the end" for every
  kan! level: its notes live in the score, not in the beatmap's hit
  objects. kan! turns those checks off and runs its own
  (`KanBeatmapVerifier`, editor-pipeline.md §6h).
- **Merge-conflict cost:** low; one virtual method and a filter in
  `IssueList.Refresh`.
- **Upstream-PR:** candidate (any ruleset with objects outside the beatmap).

### F-20 (no osu! servers): offline until kan! has its own

- **What:** `Online/KanEndpointConfiguration.cs` (new): every address empty,
  `HasServer` false; `OsuGameBase.CreateEndpoints` always returns it.
  `APIAccess.HasLogin` is false without an API address (the game stays
  offline, requests fail at once). Guards on `HasServer`: the main menu's
  online banner (`OnlineMenuBanner`), the "become an osu!supporter" note
  (`SupporterDisplay`), the ranked-beatmap metadata download
  (`LocalCachedBeatmapMetadataSource.FetchCache`, from assets.ppy.sh).
  The updater (`VelopackUpdateManager`) reads github.com/KairoPhos13/kan's
  releases, not osu!'s. Discord rich presence is off (it is osu!'s Discord
  application). Windows file and link associations are not installed
  (`Program.cs`): osu!'s `.osz`/`.osr`/`.osk` and `osu://` under osu!'s own
  registry key would be taken from an installed osu!, and removed from it
  when kan! is uninstalled.
- **Why:** owner 2026-10-27, before a public release. A modified client
  must not use osu!'s services; an updater pointed at osu! would install
  osu!lazer over kan!. The online code is all kept: filling in
  `KanEndpointConfiguration` with kan!'s own servers (osu-web,
  osu-server-spectator) turns it back on.
- **Verified:** an isolated run's logs show no request to any ppy.sh host
  (before: the metadata cache from assets.ppy.sh).
- **Merge-conflict cost:** low; one new file and one-line guards.
- **Upstream-PR:** no (kan!-specific).

### F-21: kan!'s art and sounds over osu!'s resources

- **What:** `KanBrandResources.cs` (new): a resource store over the
  `brand` folder next to the executable, added to `Resources` ahead of
  `osu.Game.Resources` (`OsuGameBase`). `osu.Desktop.csproj` copies
  `game/brand/` (kan-rhythm repository) into it, when that folder exists.
- **Why:** osu!'s resources are CC BY-NC 4.0 and its name and logo are
  trademarks; kan! replaces them one file at a time, each file named as
  the resource it replaces (`game/brand/README.md`). A missing file falls
  back to osu!'s.
- **Verified:** a test `Textures/Menu/logo.png` showed as the main menu's
  logo in an isolated run.
- **Merge-conflict cost:** low; one new file, three lines in
  `OsuGameBase`, one item group in the project file.
- **Upstream-PR:** no.

### F-22: no osu! voice or theme music; gold logo disc; provisional icons

- **What:** `IntroScreen`: the menu voice ("welcome to osu!", "see ya")
  and osu!'s theme track are never played, whatever the stored settings
  say; the intro plays one of the player's own levels, or starts silent
  (`use_themed_intro` is the switch for when kan! has its own intro
  music). `IntroTriangles`: the text says "welcome to kan!".
  `MainMenuSettings`: the "Interface voices" and "osu! music theme"
  toggles are hidden. `OsuLogo`: the disc under the logo is gold (was
  pink). App icons (`osu.Desktop/lazer.ico`, `beatmap.ico`,
  `assets/lazer.png`, `assets/lazer-nuget.png`) replaced with kan!'s
  provisional logo, generated by `game/tools/gen-provisional-brand.py`,
  which also writes the logo, intro and menu backgrounds into
  `game/brand/` (F-21).
- **Why:** owner (2026-11-09): mute the "welcome to osu" intro and its
  music before the release; osu!'s voice, music and logo are not ours
  (CC BY-NC 4.0, trademarks). Logo design by the owner: a white ring
  whose disc fades to gold at the centre, "kan!" in white with silver
  borders.
- **Verified:** isolated run: the intro draws the kan! outline and says
  "welcome to kan!" with no voice; the main menu shows the gold logo
  over kan!'s backgrounds.
- **Merge-conflict cost:** low; a few lines in four files, binary icons.
- **Upstream-PR:** no.

### F-23 (platform builds, 2026-11-09): no fork change needed

- **What:** nothing: **no file in the fork was touched**. Windows/macOS
  release builds were added in the kan-rhythm repository
  (`.github/workflows/build.yml`, `game/packaging/macos/`: the `kan!.app`
  bundle with `Info.plist`, `NSMicrophoneUsageDescription` and the hardened
  runtime entitlements, assembled in CI). Recorded here so the platform work
  is traceable in this log; the pin-log row above records that the submodule
  pin did not move. Audit of platform assumptions:
  `game/docs/platform-builds.md`.
- **Why:** the packaging surface upstream osu!lazer keeps outside its
  repository (Velopack/osu-deploy) is exactly the surface kan! needs; doing
  it in CI means zero extra divergence. Checked for platform assumptions in
  every kan! delta (F-1…F-22): the brand copy (`osu.Desktop.csproj`) is
  plain MSBuild, the F-20 endpoint/association code is already
  platform-guarded, and the ruleset loader and `DllImport` names resolve on
  all three platforms (documented in platform-builds.md §6).
- **Merge-conflict cost:** none.
- **Upstream-PR:** no (but a generic "external macOS bundle" helper could be
  useful upstream; not offered).
### F-24: kan!'s own server, and never osu!'s

- **What:** `KanEndpointConfiguration` takes the server address from
  `KAN_SERVER_URL` (development, tests, community servers) or a built-in
  default (`https://kan-api.kairophos.link`, kan!'s server since
  2026-11-10; empty before that; `kan.kairophos.link` is the website), with a public OAuth
  client (`kan-client` / `kan-public-client`, like osu!lazer's, shipped in
  every copy). `UsesOsuAssets` (always false) now guards the menu banner,
  the supporter note and osu!'s ranked-metadata cache, which come from
  osu!'s servers whatever the API address is (F-20 guarded them by
  `HasServer`). `TrustedDomainOnlineStore` trusts only kan!'s server host
  (was `*.ppy.sh`). Previews come from `{server}/preview/{id}.mp3`, none
  without a server (was `b.ppy.sh`). `DrawableAvatar` no longer falls back
  to `a.ppy.sh`. First-run setup's beatmap step (`ScreenBeatmaps`) no
  longer downloads osu!'s tutorial and bundled osu! beatmaps: it opens
  kan!'s level catalogue (with a server) or explains how to add levels.
- **Why:** owner (2026-11-09): accounts, leaderboards and downloading
  songs from the start, on kan!'s own small server (`server/app`, which
  speaks the client's protocol: `server/docs/lazer-protocol.md`). Turning
  the API on must not turn on any osu! address.
- **Verified:** build 0 warnings / 0 errors. Isolated run against the
  local server behind a test TLS proxy: login with a saved token,
  `GET me`, favourites and friends answered by kan!'s server; realtime
  hubs and notifications fail quietly and retry (not implemented, as
  documented); no request to any `ppy.sh` host. Not yet verified in game:
  catalogue download, online match of a downloaded level, score
  submission (next, against the deployed server).
- **Merge-conflict cost:** low; one kan! file, a few lines in six files;
  `ScreenBeatmaps` rewritten (upstream changes there need a manual look).
- **Upstream-PR:** no.

### F-26: account creation without osu!'s policy and promises (2026-11-10, commit `bb17154f0b`)

- **What:** `ScreenWelcome` goes straight to the form (`ScreenEntry`);
  osu!'s `ScreenWarning` (one account per person, osu!'s help centre link)
  is no longer reached but stays in the tree. `ScreenEntry`'s email note
  says what kan!'s server really does: the address only tells accounts
  apart, is never shown, and no email is sent (no verification, no password
  reset). English only: the note is a kan! string, not a localised one.
- **Why:** the warning linked to a help page kan!'s server does not have
  and stated osu!'s rules; the email note promised notifications and
  password recovery kan! does not offer.
- **Merge-conflict cost:** low; one line in `ScreenWelcome`, two in
  `ScreenEntry`.
- **Upstream-PR:** no.

### F-27: quiet connectors when realtime services do not exist (2026-11-10, commit `ef737e8068`)

- **What:** `PersistentEndpointClientConnector.BuildConnectionAsync` may return null, meaning "the
  remote service has no endpoint to connect to". The connector then makes no connection attempt,
  retries nothing, logs one "the service is not available" line, and settles in the disconnected
  state (`IsConnected` false) so UIs read it as the service being not available rather than still
  connecting. `HubClientConnector` reports this for an empty endpoint URL (spectator, multiplayer and
  metadata all share it), and the notifications connector when `GET /api/v2/notifications` gives no
  `notification_endpoint` or does not answer, which also removes the null dereference that used to
  fire after a 404. The "connecting..." log line moved after the build, so an unavailable service
  never claims to be connecting. `osu.Game/Tests/TestChatClientConnector` only follows the new
  signature (it polls over HTTP and always connects).
- **Why:** owner (2026-11-10): login works fine, but the network log shows every minute or so
  `OnlineMetadataClient` / `SpectatorClient` / `OnlineMultiplayerClient connect attempt failed ...
  System.UriFormatException: Invalid URI: The URI is empty.` and a `NullReferenceException` in the
  notifications connector's `BuildConnectionAsync`, each retried with exponential back-off (3 s to
  120 s). kan!'s server runs no realtime services (`KanEndpointConfiguration` leaves the three hub
  URLs empty on purpose), so there is nothing to connect to and nothing to retry. One guard at the
  connector level covers every call site.
- **Verified:** build green, 0 warnings / 0 errors. Traced what the login panel waits on: `LoginPanel`
  shows its waiting UI from `api.State` alone (spinner with "Connecting..." or "Attempting to
  reconnect"), and its signed-in `UserRankPanel` waits on `LocalUserStatisticsProvider` statistics.
  Neither waits on these connectors, and the connectors cannot block login (they run off-thread and
  only log). Not yet run against the deployed server; the companion server routes (implemented-empty
  notifications, chat ack and seasonal backgrounds) remove the 404s the owner saw.
- **Merge-conflict cost:** low: the abstract method's nullability in one base file plus three small
  implementations and one test signature. Upstream edits to `BuildConnectionAsync` need a manual look.
- **Upstream-PR:** no. A "no endpoint to connect to" outcome is kan!'s deployment shape; upstream
  deployments always have all endpoints. If upstream ever wants partial service deployments, the
  null-returning builder could be proposed there.
### F-28: osu!-only features cleared from the UI (2026-11-10, commit `435757ed2a`)

- **What:** the osu!stable import is never offered. `FirstRunSetupOverlay`
  only adds the `ScreenImportFromStable` step when
  `KanEndpointConfiguration.UsesOsuAssets` is true (never), and
  `BatchImportSettings` (settings, debug builds, "Batch Import") hides
  its "Import ... from stable" buttons the same way. The two osu!-only
  links in settings (general section, quick actions) are hidden:
  "Learn more about lazer" (osu!'s wiki) and "Report an issue" (osu!'s
  forums). The dead "tutorial" settings-search keyword on "Run setup
  wizard" is swept. The osu! tutorial button needed no new delta: F-24's
  rewrite of `ScreenBeatmaps` already removed "Get the osu! tutorial"
  (with the bundled osu! beatmaps download); this change verifies it is
  gone. Upstream code paths stay: `ScreenImportFromStable`, the
  `BatchImportSettings` buttons, the `LegacyImportManager` importer, and
  the osu!stable locations they read (registry keys, `osu!` folders,
  F-3). The now-unused strings are left to the string sweep lane.
- **Why:** issue 24, before the first public release. kan! is a
  standalone game (ADR-0011) and plays `.kan` levels, not osu! beatmaps
  (conversion is future work in `tools/`); importing from osu!stable
  promised something kan! does not do, kan! has no tutorial yet
  (`game/docs/gameplay.md` is the seed), and the two links led players
  into osu!'s wiki and forums. What replaces them (a kan! level importer,
  a kan! tutorial, kan!'s own help and issue tracker) is the maintainer's
  call.
- **Verified:** `make check` green (0 warnings / 0 errors). Code
  verified: the only paths composing the import step or the import
  buttons sit behind `UsesOsuAssets` (always false), and no settings or
  first-run surface links to osu! any more. A running-client check of the
  first-run flow and the settings overlay was not done here (the game is
  not launched on this build machine): the acceptance criterion
  "verified in a running client" is left for the maintainer.
- **Merge-conflict cost:** low; one extra condition on an existing `if`
  in two files, one removed keyword, two removed settings buttons, one
  removed using, and a doc line in a kan!-authored file.
- **Upstream-PR:** no (product-scope decision).

### String sweep, first pass (2026-10-27)

The game's own name in English strings that mean the game itself:
first-run setup, settings, folders, fullscreen, exit dialog, update and
audio notifications (`Localisation/*Strings.cs`, each delta marked); the
startup error for old Windows (`Program.cs`); the editor's title
(`EditorMenuBar`: kan!forge); the web user agent (`OsuWebRequest`,
`OsuJsonWebRequest`). Strings about osu!-only things (osu!stable import,
osu!supporter, Featured Artists, the osu! music theme) are left: those
features are not kan!'s.

### String sweep, second pass (2026-11-10, commit `e000468941`, issue 22)

- **What:** the remaining English strings where "osu!" means the game
  itself now say kan!, each delta marked `kan! rebrand (ADR-0011)`:
  - `osu.Desktop/NVAPI.cs`: the NVIDIA profile matches `kan.exe` (was
    `osu!.exe`) and its names use the neutral `kan` spelling (was `osu!`);
  - `osu.Desktop/Program.cs`: the multiple-running-instances message;
  - `osu.Desktop/Windows/WindowsAssociationManager.cs`: the application
    capability description is `kan!` (was `osu!(lazer)`);
  - `osu.Game/Localisation/`: `StorageErrorDialogStrings` (storage error
    and data location copy), `NamedOverlayComponentStrings` (changelog
    tooltip), `BeatmapOverlayStrings` (user content disclaimer);
  - `osu.Game/Database/RealmAccess.cs` (database recovery messages) and
    `osu.Game/OsuGameBase.cs` (data migration log);
  - `osu.Game/Seasonal/IntroChristmas.cs`: "welcome to kan!" and
    "merry kan!mas!" (was "welcome to osu!" and the osu!-mas pun);
  - `osu.Game.Tournament/Screens/Setup/TournamentSwitcher.cs`:
    "Close kan!" (was "Close osu!").
  Kept deliberately, each triaged under the issue's rule (keep what
  refers to actual osu! or is attribution):
  - osu!stable import and its paths: `DialogStrings`,
    `FirstRunOverlayImportFromStableScreenStrings`,
    `MaintenanceSettingsStrings` (stable location),
    `LegacyCollectionImporter`/`LegacyModelImporter`/`LegacyImportManager`/
    `StableStorage`/`OsuGameDesktop` (locating the user's osu! install),
    the tournament client's osu!stable data source copy;
  - osu!supporter and osu!'s site: `LeaderboardStrings`,
    `OnlinePlayStrings`, `UserInterfaceStrings` (supporter note),
    `SupporterDisplayStrings`, `ChangelogSupporterPromo`,
    `DrawableRecentActivity`, `ScreenQueue` ("osu! forums"),
    `ScreenWarning`'s osu! account policy (unreachable since F-26);
  - other osu!-only things: Featured Artists
    (`BeatmapOverlayStrings.FeaturedArtistsTooltip`), "Get the osu!
    tutorial" (`FirstRunSetupBeatmapScreenStrings`), "osu! music theme"
    (`UserInterfaceStrings`, toggle hidden since F-22), "classic versions
    of osu!" (`FirstRunSetupOverlayStrings`), the osu! beatmap export
    disclaimer (`BeatmapSubmissionStrings`), the "osu!direct" menu tip
    (`MenuTipStrings`), `ModScoreV2` (marks osu!stable scores), the
    stock osu!/taiko/catch/mania ruleset strings and matchmaking pool
    names (not shipped since F-9), `Localisation/Osu/*` (osu! ruleset);
  - osu!'s built-in skins' names and "team osu!" creator, bundled beatmap
    titles ("... (osu! edit).osz"), and attribution text
    (`KanAboutSettings` "Built on osu!lazer (MIT), ppy/osu contributors",
    `THIRD_PARTY_NOTICES`, upstream licence headers);
  - developer configuration and platform projects kan! does not build:
    `osu.Desktop/Properties/launchSettings.json` (IDE run profiles; JSON
    cannot carry the marker comment), `.vscode`/`.idea`/`.run`,
    `Templates/*`, `osu.Android`/`osu.iOS` (desktop only, F-23), and
    upstream package metadata (`osu.nuspec`, `osu.Game.csproj`'s NuGet
    title: the ppy/osu package identity is upstream's, ADR-0011 §4).
- **Why:** issue 22 (the owner met leftover osu! copy on the mod
  selection screen); every "osu!" that means this game must say kan!
  before release (ADR-0011 §1).
- **Verified:** `make check` green (0 warnings / 0 errors; native 2/2,
  Kan.Core 130, Kan.Forge 48). `git grep -i "osu!"` over the fork triaged
  hit by hit; the list above is the triage record.
- **Merge-conflict cost:** low; one to three lines in eleven files, all
  strings and comments, no behaviour.
- **Upstream-PR:** no (product-specific strings).

Judgement calls in the second pass: the mobile disclaimer
(`ButtonSystemStrings`) keeps "we have released osu! on mobile platforms":
it is a statement about osu!, shown only in mobile builds kan! does not
ship (F-23), and saying kan! there would claim a mobile release that does
not exist (reviewed 2026-11-10). The NVIDIA profile names use the neutral `kan`
spelling of the brand (like the `kan` assembly name of ADR-0011 §4), not
the `kan!` wordmark.

### F-29: a ruleset wedge in song select (2026-11-11, commit `da2f355dad`)

- **What:** `Ruleset.CreateSongSelectWedgeContent(Action refilter)`
  (default null) lets a ruleset add its own wedge to song select, under
  the beatmap's title and difficulty wedge. New `RulesetWedge`
  (`Screens/Select/RulesetWedge.cs`) hosts it with the wedges' background
  and enter/leave motion, rebuilds it when the ruleset changes, and takes
  no space when the ruleset adds nothing. `SongSelect` places it,
  shows/hides it with the title wedge, and subtracts its height from the
  details area. `FilterControl.Refilter()` filters again with the same
  controls, for a ruleset setting the filter reads.
- **Why:** owner 2026-11-11: the instrument and its range were chosen in
  the player loader's side panel (F-16), found only by hovering at the
  right of that screen; choosing them where the level is chosen "is more
  intuitive". kan!'s wedge (`KanInstrumentWedge`) shows the instrument,
  its range and how the selected level fits it; a click opens the choices
  in a popover, and a change filters the list again (the "only levels my
  instrument plays" filter). The player loader's group keeps the notation
  only.
- **Verified:** client and ruleset build green (0 warnings / 0 errors).
  Isolated instance: the wedge under the title wedge, the popover open,
  the player's range kept when it opens.
- **Merge-conflict cost:** low; one new file, one virtual on `Ruleset`,
  one public method on `FilterControl`, four lines in `SongSelect`.
- **Upstream-PR:** no (kan!-specific need).

### F-30: level submission on kan!'s server (2026-11-11, commit `b957a67a74`)

- **What:** `KanEndpointConfiguration.BeatmapSubmissionServiceUrl` is
  `{APIUrl}/beatmap-submission` when kan! has a server (was null), so
  lazer's own in-editor submission flow ("Submit level...") talks to
  kan!'s server (`server/docs/lazer-protocol.md`). `Editor` offers it for
  kan! levels as well as the legacy rulesets, and asks the ruleset
  whether a level is empty (`Ruleset.IsEmptyForSubmission`, default: no
  hit objects; kan!'s notes live in the level's score). The wizard's
  English strings speak of levels and kan!'s content rule; its buttons
  open the level creator's handbook and kan!'s issue tracker
  (`KanEndpointConfiguration.HANDBOOK_URL`, `ISSUES_URL`) instead of
  osu!'s wiki and forums. The options kan! cannot honour yet are hidden
  behind `UsesOsuAssets`: discussion reply notifications, opening the
  level's page after submission, and the editor's info and discussion
  page items.
- **Why:** owner 2026-11-11: levels must reach players from their
  creators, credited to them ("the status of the level maker is as
  important as the leaderboard"), with osu!'s own system rather than a
  separate upload path.
- **Verified:** client and ruleset build green (0 warnings / 0 errors).
  Isolated instance against a local kan! server behind a test TLS proxy:
  signed in, opened a level in the editor, "Submit level..." through
  the wizard; the server reserved the ids, stored the package (with
  `kan.score.json`, design, credits and audio) and the result card showed
  "mapped by" the submitter as WIP.
- **Merge-conflict cost:** low; one URL, one condition in `Editor`, one
  virtual on `Ruleset`, string defaults, three link targets and three
  hidden controls.
- **Upstream-PR:** no (kan!-specific server).

### F-25: `README.md` rewritten for the kan! game client (2026-11-09, commit `4fa5313`)

- **What:** the fork root `README.md` (upstream osu!'s) is replaced with
  kan!'s own player-facing README.

## String sweep: remaining scope (deferred, game/TODO.md)

- Translations: the strings change only in English. Other languages come
  from `osu.Game.Resources`' translations and still say osu! (issue 23).
- **Pending maintainer decision (not changed):** `osu.Game/OsuGameBase.cs`
  `OSU_PROTOCOL = "osu://"` (keep for compatibility or move to `kan://`).
- **Pending maintainer decision (not changed):** kan!'s own file types and
  link scheme naming. Until it lands,
  `osu.Desktop/Windows/WindowsAssociationManager.cs` and
  `osu.Game/Localisation/WindowsAssociationManagerStrings.cs` keep osu!'s
  `.osz`/`.olz`/`.osr`/`.osk` and `osu://`/`osump://` and their
  descriptions ("osu! Beatmap", "osu! Replay", "osu! Skin", "osu!",
  "osu! Multiplayer"), which name osu!'s formats and schemes; the
  application description already says kan! (second pass).
- osu!-only features: none reachable since F-22 (the osu! music theme),
  F-24 (the osu! tutorial button) and F-28 (the osu!stable import in
  first-run setup and settings, the osu!-only settings links); their
  unused strings are left to this sweep
- Default skin/logo/triangle assets and the `lazer.ico` app icon: replaced
  through `game/brand/` (F-21) once kan!'s are made
