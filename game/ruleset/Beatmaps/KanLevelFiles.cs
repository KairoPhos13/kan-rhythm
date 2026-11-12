// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Core.Design;
using Kan.Core.Score;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// Reads kan! files that travel inside a level (the score) where no
    /// dependency injection reaches: the beatmap converter. The game's
    /// storage is captured by the ruleset icon (<see cref="KanRulesetIcon"/>),
    /// which the toolbar loads when the game starts, before any level can be
    /// played.
    /// </summary>
    public static class KanLevelFiles
    {
        private static Storage? storage;
        private static osu.Game.OsuGame? game;

        public static void Init(Storage gameStorage, osu.Game.OsuGame? osuGame)
        {
            storage ??= gameStorage;
            game ??= osuGame;
        }

        /// <summary>
        /// Whether the editor is the screen loading (2026-10-15: the editor
        /// crashed opening a level with a score). The editor keeps the level's
        /// own objects: it saves them in the osu! format, which cannot hold a
        /// kan! note, and its compose tab edits the score file directly.
        /// </summary>
        public static bool EditorLoading => game?.ScreenStack?.CurrentScreen is osu.Game.Screens.Edit.EditorLoader or osu.Game.Screens.Edit.Editor;

        /// <summary>
        /// The level's score: the copy inside the level, or the editor's
        /// autosave when it is newer (the same rule the compose tab follows).
        /// Null when the level has none, or the storage is not known yet.
        /// </summary>
        public static KanScore? LoadScore(BeatmapSetInfo? set)
        {
            if (storage == null || set == null) return null;

            var inLevel = readLevelFile(set, KanScore.FILE_NAME) is string json ? KanScore.FromJson(json) : null;
            var autosaved = loadAutosave(set, @"score.json", KanScore.Load);

            return Kan.Core.LevelDocument.Newer(inLevel, autosaved);
        }

        /// <summary>
        /// The level's design: the copy inside the level, or
        /// the editor's newer autosave; null when the level has none (the
        /// caller generates one from the notes, <see cref="AutoDesign"/>).
        /// </summary>
        public static KanDesign? LoadDesign(BeatmapSetInfo? set)
        {
            if (storage == null || set == null) return null;

            var inLevel = readLevelFile(set, KanDesign.FILE_NAME) is string json ? KanDesign.FromJson(json) : null;
            var autosaved = loadAutosave(set, @"design.json", KanDesign.Load);

            return Kan.Core.LevelDocument.Newer(inLevel, autosaved);
        }

        /// <summary>The design to play: the level's, else one generated from its notes.</summary>
        public static KanDesign DesignFor(BeatmapSetInfo? set, IReadOnlyCollection<ScoreNote> notes, double levelLengthMs)
            => LoadDesign(set) is { } design ? AutoDesign.Refresh(design, notes, levelLengthMs) : AutoDesign.Generate(notes, levelLengthMs);

        private static string? readLevelFile(BeatmapSetInfo set, string fileName)
        {
            try
            {
                string? path = set.GetFile(fileName)?.File.GetStoragePath();
                if (path == null) return null;

                using var stream = storage!.GetStorageForDirectory(@"files").GetStream(path);
                if (stream == null) return null;

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception e)
            {
                osu.Framework.Logging.Logger.Log($"kan!: the level's {fileName} could not be read: {e.Message}");
                return null;
            }
        }

        private static T? loadAutosave<T>(BeatmapSetInfo set, string fileName, Func<string, T?> load) where T : class
        {
            try
            {
                return load(Path.Combine(storage!.GetFullPath(@"kan-forge"), set.ID.ToString("N"), fileName));
            }
            catch
            {
                // The level copy is the source of truth; the autosave only helps its author.
                return null;
            }
        }

        /// <summary>The kan-forge folder (timing.json), or null before the storage is known.</summary>
        public static string? ForgeRoot => storage?.GetFullPath(@"kan-forge");
    }

    /// <summary>The ruleset's icon; on load it also hands the game's storage to <see cref="KanLevelFiles"/>.</summary>
    public partial class KanRulesetIcon : CompositeDrawable
    {
        public KanRulesetIcon()
        {
            // Sized by whoever shows it (toolbar, song select set panels set
            // Size), like a stock ruleset icon: no auto-size, the glyph fills it.
            Size = new osuTK.Vector2(20);
            InternalChild = new SpriteIcon { Icon = FontAwesome.Solid.Music, RelativeSizeAxes = Axes.Both };
        }

        [BackgroundDependencyLoader(true)]
        private void load(Storage? storage, osu.Game.OsuGame? game, GameHost? host, osu.Game.Rulesets.IRulesetConfigCache? configs)
        {
            if (storage != null) KanLevelFiles.Init(storage, game);

            // The player's instrument and range, known from here on (the drawable ruleset fits levels with it).
            if (configs?.GetConfigFor(new KanRuleset()) is Kan.Ruleset.Configuration.KanRulesetConfigManager config)
                Kan.Ruleset.Configuration.KanPlayer.Bind(config);

            // Developer tooling (inert unless its environment variable is set). It
            // lives on the game itself: the toolbar holding this icon may be hidden.
            if (Dev.KanDevPlay.Enabled && !devPlayStarted && game != null && host != null)
            {
                devPlayStarted = true;
                host.UpdateThread.Scheduler.Add(() => game.Add(new Dev.KanDevPlay()));
            }

            // The level's part in the song select preview (game-wide, like the above).
            if (!previewStarted && game != null && host != null)
            {
                previewStarted = true;
                host.UpdateThread.Scheduler.Add(() => game.Add(new Kan.Forge.Play.LevelPreview(KanLevelFiles.LoadScore, () => KanLevelFiles.ForgeRoot)));
            }

            // The starter pack's levels, imported once per pack version (issue
            // 15): a new player finds levels to play without an account.
            if (!packStarted && game != null && host != null)
            {
                packStarted = true;
                host.UpdateThread.Scheduler.Add(() => game.Add(new KanStarterPack()));
            }

            // kan!'s first-run guide and the calibration screen (issues 16 and
            // 17): shown once on a fresh install, and runnable any time from
            // Settings > kan!.
            if (!firstRunStarted && game != null && host != null)
            {
                firstRunStarted = true;
                host.UpdateThread.Scheduler.Add(() => game.Add(new Kan.Ruleset.FirstRun.KanFirstRun()));
            }
        }

        private static bool devPlayStarted;
        private static bool previewStarted;
        private static bool packStarted;
        private static bool firstRunStarted;
    }
}
