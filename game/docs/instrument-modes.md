# Instrument modes — design direction (owner, 2026-10-12)

- **Status:** owner direction of record. Executed partially (stock game modes
  removed — fork F-9/F-10 in [fork-divergence.md](fork-divergence.md));
  remaining work tracked in [../TODO.md](../TODO.md). The owner will describe
  the second work item later; open questions below await it.
- **Source:** owner decision in the game-client working session, 2026-10-12.

## 1. Direction

**Game modes (osu!standard, taiko, catch, mania) are gone from the kan!
client. Their place — the mode selector — belongs to INSTRUMENTS.**

Rationale (owner): instrument identity is what drives the product — it
selects the judgment profile (attack type, windows), the **instrument-wise
audio effects**, the helper visuals, and the scoring/leaderboard dimensions.
"You select the instrument" is therefore a *mode-level* choice, not a skin or
a setting.

Consequences already executed:

- The stock ruleset projects are no longer referenced by the desktop client
  (fork F-9); osu!stable legacy IPC — which hardcodes the stock mode IDs —
  is removed with them (fork F-10). The projects stay in the fork repo for
  tests/tooling only.

## 2. Instrument scope (owner, 2026-10-12)

| Priority | Instrument | Category | Notes |
|---|---|---|---|
| MVP now | **piano** | Keys (universal profile) | "pretty universal" — one profile, no variants |
| MVP now | **ocarina** | **winds/others** | many ocarina types exist (variants matter) |
| Traction later | bass | TBD (strings?) | explicitly named by owner |
| Traction later | guitar | TBD (strings?) | explicitly named by owner |
| Traction later | drums | TBD (percussion?) | explicitly named by owner |

Category facts from the owner:

- Ocarina belongs in the **"winds/others"** category (not its own top-level
  category).
- There will be *many* instruments per category — many ocarina types, many
  winds, and "in general a lot of instruments in the others category".
- Therefore a **personalization menu** will be needed to choose which
  instruments appear — explicitly deferred ("for the future").

## 3. Requirements this creates

1. **Instrument selection** must be possible wherever play starts (song
   select / play setup), because instrument-wise effects and judgment
   depend on it.
2. **Variants matter within an instrument**: ocarina = many types (e.g.
   different keys/ranges/transpositions); piano = one universal profile.
   This matches the existing *instrument pack* concept
   ([platform-architecture.md §5](../architecture/platform-architecture.md)):
   a pack carries the profile (attack type, range, transient-blanking,
   judgment-window offsets), the technique vocabulary, fingering renderers,
   intonation config and validation templates.
3. **Instrument-wise effects**: selecting the instrument selects the kanfx
   bus-chain preset (e.g. piano = universal chain; winds = air/attack-aware
   chains; per-variant tuning compensation). Selection → pack → FX +
   judgment + visuals, one source of truth.
4. **Personalization menu** (future): the instrument list gets long, so the
   player chooses which instruments/categories are enabled and visible.
5. **Instrument selector, category-tied** (owner, 2026-10-13): an additional
   selector to choose an instrument, where the instrument list is tied to
   the selected category (category → instrument).
6. **Persistence in local files** (owner, 2026-10-13): the selection must be
   saved locally so the program remembers it from the last run.

### v0 realised (2026-10-13) — selector + persistence

Requirements 5 and 6 are implemented in the ruleset plugin (zero fork
divergence):

- **Selector UI:** Settings → Rulesets → kan! (`KanSettingsSubsection`):
  a *Category* dropdown (`Keys`, `Winds/others`) and an *Instrument*
  dropdown whose entries are filtered by the selected category
  (`KanInstrumentCatalog` is the single source of truth for the
  category ↔ instrument tie and display names). Changing category snaps the
  instrument to the category's first entry if needed.
- **Persistence:** osu!lazer's standard ruleset-settings mechanism
  (`KanRulesetConfigManager` : `RulesetConfigManager<>`) — the selection is
  stored in the local **`client.realm`** (game storage, e.g.
  `~/.local/share/kan/client.realm`) and reloaded on startup. No custom file
  handling needed; values survive restarts.
- **Provisional defaults:** category `Keys`, instrument `Piano` (owner has
  not specified a startup instrument yet).
- **Still open:** the final placement of the selector at play start (song
  select / play setup), the variant selector (ocarina types), and wiring the
  selection to instrument-wise effects (kanfx packs) once they exist — all
  tracked in `../TODO.md`.

## 4. Architecture options for "instruments in the mode selector"

| Option | Shape | Pros | Cons |
|---|---|---|---|
| A. One ruleset plugin per instrument | `KanPiano`, `KanOcarina`, … each a ruleset | Mode bar literally shows instruments; per-instrument first-class | More assemblies |
| B. One kan ruleset + selector inside | single `Kan` ruleset; instrument chosen in song select | Simplest assembly layout | Mode bar shows "kan!", not instruments |
| C. **Hybrid (recommended)** | shared `Kan.Ruleset.Core` (judgment, highway, effects wiring, pack loading) + thin per-instrument ruleset facades; enabled facades = the personalization menu | Matches the owner's wording (instruments in the mode slots) *and* keeps all logic in one core; packs stay data (platform-architecture §5) | Facade assemblies must follow the loader naming rule |

**Loader constraint (design input, verified in the fork's source):**
`RulesetStore` discovers only DLLs named `osu.Game.Rulesets.*.dll` and
registers exactly **one** ruleset per assembly (first public `Ruleset`
subclass). So option A/C need one thin assembly per enabled instrument
(e.g. `osu.Game.Rulesets.KanPiano.dll`, `osu.Game.Rulesets.KanOcarina.dll`)
— cheap if the facades are a few dozen lines over the shared core.

Existing mapping stays valid ([osu-integration.md §3](../architecture/osu-integration.md)):
Layer 3 `.kchart` = one difficulty per instrument chart; the instrument pack
referenced by chart metadata; mods reuse osu! `Mod` classes.

## 5. Editor interplay (level generation)

The owner works on the level editor (kan!forge, Milestone 3) in parallel —
it is the vehicle for *generating* levels. Implications for this workstream:

- The editor authors Layer 3 `.kchart` **per instrument** (incl. fingering
  fields) — it needs the same instrument taxonomy/selection.
- The transcription pipeline already in the monorepo
  (`tools/chart_scribe`, `kan_demo --transcribe` MIDI + log) provides draft
  notes; the editor's `.mid` importer (Milestone 3) should consume that
  output directly.
- One source of truth for the taxonomy: the editor and the client must
  share the instrument/category model (proposal: `charts/spec` defines it,
  both consume).

## 6. Open questions (awaiting the owner's next description)

1. Exact category taxonomy and naming ("winds/others" as a combined
   category? where do bass/guitar/drums land?).
2. Behavior of the personalization menu (per-instrument toggle? per
   category? ordering?).
3. Confirm option C (facade rulesets over shared core) vs B (selector
   inside one ruleset).
4. Scoring identity: how instrument + variant map to
   `input_method`/leaderboard dimensions (server/ `backend-data-model.md`).
5. Whether instrument variants (ocarina types) are *packs of one instrument*
   or distinct selector entries.
