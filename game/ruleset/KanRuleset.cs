// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit.Setup;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Configuration;
using Kan.Ruleset.Difficulty;
using Kan.Ruleset.Legacy;
using Kan.Ruleset.UI;
using Kan.Forge.Editor;

namespace Kan.Ruleset
{
    /// <summary>
    /// The kan! game mode: real-instrument play with dual-axis judgment
    /// (attack timing + intonation). Phase 0 skeleton — this class only proves
    /// the osu!lazer ruleset plugin path and carries product metadata.
    /// Gameplay arrives with the "Input & judgment core" work items in
    /// game/TODO.md (Phase 1).
    /// </summary>
    /// <remarks>
    /// The upstream base type is spelled fully qualified throughout this
    /// project: inside the <c>Kan.Ruleset</c> namespaces the simple name
    /// <c>Ruleset</c> resolves to our own namespace and shadows the upstream
    /// type. Same for <c>osu.Game.Rulesets.RulesetInfo</c> where ambiguous.
    /// </remarks>
    public class KanRuleset : osu.Game.Rulesets.Ruleset, osu.Game.Rulesets.ILegacyRuleset
    {
        /// <summary>
        /// kan! claims legacy ruleset ID 0 (osu!standard) so legacy `.osu`/
        /// `.osz` content stays importable and convertible into kan! charts
        /// (osu-integration.md §3). QA finding 2026-10-13: without a loaded
        /// ruleset claiming the legacy ID, the beatmap decoder rejects every
        /// legacy beatmap ("Rulest is not available locally.") — which became
        /// visible when the stock modes left the client (fork F-9). Taiko/
        /// catch/mania (legacy IDs 1–3) are intentionally unclaimed for now.
        /// </summary>
        public int LegacyID => 0;

        public ILegacyScoreSimulator CreateLegacyScoreSimulator() => new KanLegacyScoreSimulator();

        /// <summary>Product tagline (ADR-0011), shown by the placeholder screen and the about screen.</summary>
        public const string TAGLINE = @"You kan!";

        private static int loadLogged;

        public KanRuleset()
        {
            // One-shot breadcrumb so logs prove the plugin path worked (and so
            // user bug reports show whether their ruleset was picked up).
            if (Interlocked.Exchange(ref loadLogged, 1) == 0)
                Logger.Log(@"kan! ruleset loaded (Phase 0 skeleton — judgment core pending)", level: LogLevel.Important);
        }

        public override string Description => @"kan!";

        public override string ShortName => @"kan";

        /// <summary>
        /// The grades a note can get: osu!mania's steps (owner 2026-10-15).
        /// Only these are listed in the results.
        /// </summary>
        public override IEnumerable<osu.Game.Rulesets.Scoring.HitResult> GetValidHitResults() => new[]
        {
            osu.Game.Rulesets.Scoring.HitResult.Perfect,
            osu.Game.Rulesets.Scoring.HitResult.Great,
            osu.Game.Rulesets.Scoring.HitResult.Good,
            osu.Game.Rulesets.Scoring.HitResult.Ok,
            osu.Game.Rulesets.Scoring.HitResult.Meh,
            osu.Game.Rulesets.Scoring.HitResult.Miss,
        };

        public override string PlayingVerb => @"Playing";

        /// <summary>
        /// Ruleset plugins are validated against the fork's API version on load
        /// (see RealmRulesetStore); always point at the current constant.
        /// </summary>
        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        /// <summary>Mods that need nothing from the level editor (owner 2026-10-15).</summary>
        public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
        {
            ModType.DifficultyReduction => new Mod[]
            {
                new Kan.Ruleset.Mods.KanModEasy(),
                new Kan.Ruleset.Mods.KanModNoFail(),
                new MultiMod(new Kan.Ruleset.Mods.KanModHalfTime(), new Kan.Ruleset.Mods.KanModDaycore()),
                new Kan.Ruleset.Mods.KanModWait(),
                new Kan.Ruleset.Mods.KanModPractice(),
            },
            ModType.DifficultyIncrease => new Mod[]
            {
                new Kan.Ruleset.Mods.KanModHardRock(),
                new MultiMod(new Kan.Ruleset.Mods.KanModSuddenDeath(), new Kan.Ruleset.Mods.KanModPerfect()),
                new MultiMod(new Kan.Ruleset.Mods.KanModDoubleTime(), new Kan.Ruleset.Mods.KanModNightcore()),
                new Kan.Ruleset.Mods.KanModHidden(),
            },
            ModType.Automation => new Mod[] { new MultiMod(new Kan.Ruleset.Mods.KanModAutoplay(), new Kan.Ruleset.Mods.KanModCinema()) },
            ModType.Fun => new Mod[] { new Kan.Ruleset.Mods.KanModMuted() },
            _ => Array.Empty<Mod>(),
        };

        public override osu.Game.Rulesets.Scoring.ScoreMultiplierCalculator CreateScoreMultiplierCalculator(osu.Game.Rulesets.Scoring.ScoreMultiplierContext context)
            => new Kan.Ruleset.Mods.KanScoreMultiplierCalculator(context);

        /// <summary>Replays hold what the judge heard (<see cref="Kan.Ruleset.Replays.KanReplayFrame"/>).</summary>
        public override osu.Game.Rulesets.Replays.Types.IConvertibleReplayFrame CreateConvertibleReplayFrame() => new Kan.Ruleset.Replays.KanReplayFrame();

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new KanBeatmapConverter(beatmap, this);

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new KanDifficultyCalculator(RulesetInfo, beatmap);

        /// <summary>The music note icon; loading it also lets the converter read level files (<see cref="KanLevelFiles"/>).</summary>
        public override Drawable CreateIcon() => new KanRulesetIcon();

        /// <summary>Misses cost health and an empty bar ends the level (owner 2026-10-15); "no fail" and "wait for me" keep it going.</summary>
        public override osu.Game.Rulesets.Scoring.HealthProcessor CreateHealthProcessor(double drainStartTime) => new Kan.Ruleset.Scoring.KanHealthProcessor(drainStartTime);

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) => new KanDrawableRuleset(this, beatmap, mods);

        /// <summary>Local persistence for the instrument selection (client.realm).</summary>
        public override IRulesetConfigManager? CreateConfig(SettingsStore? settings) => new KanRulesetConfigManager(settings, RulesetInfo);

        /// <summary>The v0 instrument selector (category-tied, persisted).</summary>
        public override RulesetSettingsSubsection? CreateSettings() => new KanSettingsSubsection(this);

        /// <summary>
        /// kan!forge level-creation flow (prompt §2.1) **merged into** the
        /// editor's stock Resources section (owner requirement 2026-10-13:
        /// minimize the seams — one organic media area). The stock rows
        /// (audio / background / video navigators) are untouched; the kan!
        /// pipeline continues below them (KanMediaSection).
        ///
        /// Mirrors the upstream default composition of
        /// <c>Ruleset.CreateEditorSetupSections</c> with ResourcesSection
        /// swapped for its kan! subclass — review when upstream changes that
        /// default list.
        /// </summary>
        public override IEnumerable<Drawable> CreateEditorSetupSections() =>
        [
            // Cover credits right under the song's metadata (owner 2026-10-14).
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(25),
                Children = new Drawable[]
                {
                    new MetadataSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                    new Kan.Forge.Editor.KanCreditsSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                },
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(25),
                Children = new Drawable[]
                {
                    // What the level is played on (owner 2026-10-15), first thing on the right.
                    new Kan.Forge.Editor.KanInstrumentSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                    new DifficultySection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                },
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(25),
                Children = new Drawable[]
                {
                    new KanMediaSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                    new ColoursSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                },
            },
            new DesignSection(),
        ];

        /// <summary>
        /// The editor's "tracks" tab (fork F-11 hook): the unified resource
        /// list with roles, the non-destructive track editor and the optional
        /// actions (owner 2026-10-13: the track editor lives next to
        /// setup/compose/design/timing/verify).
        /// </summary>
        public override osu.Game.Screens.Edit.EditorScreen? CreateTracksEditorScreen() => new KanTracksScreen();

        /// <summary>The compose tab is kan!'s score editor (fork F-13, owner 2026-10-14).</summary>
        public override osu.Game.Screens.Edit.EditorScreen? CreateComposeEditorScreen() => new Kan.Forge.Editor.Compose.KanComposeScreen();

        /// <summary>The player loader's "Notation" group (fork F-16): note names, colours, size.</summary>
        public override IEnumerable<osu.Game.Screens.Play.PlayerSettings.PlayerSettingsGroup> CreatePlayerLoaderSettings()
            => new osu.Game.Screens.Play.PlayerSettings.PlayerSettingsGroup[] { new KanNotationSettings() };

        /// <summary>Song select lists the levels the player's instrument can play.</summary>
        public override osu.Game.Rulesets.Filter.IRulesetFilterCriteria CreateRulesetFilterCriteria() => new KanFilterCriteria();

        /// <summary>kan!'s design tab (fork F-15): sections, rests, climaxes, palette and effects.</summary>
        public override osu.Game.Screens.Edit.EditorScreen? CreateDesignEditorScreen() => new Kan.Forge.Editor.Design.KanDesignScreen();
    }
}
