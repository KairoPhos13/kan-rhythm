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

> **⚠ Push blocker (2026-10-12):** the build machine has **no GitHub
> credentials** (SSH keys rejected, no HTTPS token), so commits `0f5db37` and
> `2970c68` exist only in the local submodule object store. **Before merging
> `agent/game` into `main`, the maintainer must push them:**
> `git -C game/client push origin 2970c68:refs/heads/agent/game`
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
