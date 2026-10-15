// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Forge.Score;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// Reads kan! files that travel inside a level (the score) where no
    /// dependency injection reaches: the beatmap converter. The game's
    /// storage is captured by the ruleset icon (<see cref="KanRulesetIcon"/>),
    /// which the toolbar loads when the game starts — before any level can be
    /// played.
    /// </summary>
    public static class KanLevelFiles
    {
        private static Storage? storage;

        public static void Init(Storage gameStorage) => storage ??= gameStorage;

        /// <summary>
        /// The level's score: the copy inside the level, or the editor's
        /// autosave when it is newer (the same rule the compose tab follows).
        /// Null when the level has none, or the storage is not known yet.
        /// </summary>
        public static KanScore? LoadScore(BeatmapSetInfo? set)
        {
            if (storage == null || set == null) return null;

            KanScore? inLevel = null;

            try
            {
                string? path = set.GetFile(KanScore.FILE_NAME)?.File.GetStoragePath();

                if (path != null)
                {
                    using var stream = storage.GetStorageForDirectory(@"files").GetStream(path);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        inLevel = KanScore.FromJson(reader.ReadToEnd());
                    }
                }
            }
            catch (Exception e)
            {
                osu.Framework.Logging.Logger.Log($"kan!: the level's score could not be read: {e.Message}");
            }

            KanScore? autosaved = null;

            try
            {
                autosaved = KanScore.Load(Path.Combine(storage.GetFullPath(@"kan-forge"), set.ID.ToString("N"), @"score.json"));
            }
            catch
            {
                // The level copy is the source of truth; the autosave only helps its author.
            }

            return autosaved != null && (inLevel == null || autosaved.SavedAtUtc > inLevel.SavedAtUtc) ? autosaved : inLevel;
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
        private void load(Storage? storage, osu.Game.OsuGame? game, GameHost? host)
        {
            if (storage != null) KanLevelFiles.Init(storage);

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
        }

        private static bool devPlayStarted;
        private static bool previewStarted;
    }
}
