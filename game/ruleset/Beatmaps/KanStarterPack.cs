// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using Kan.Ruleset.Configuration;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// Brings the starter pack's levels into the game the first time it runs:
    /// a player who just downloaded kan! finds levels to play, without an
    /// account and without making one (issue 15). The pack travels with the
    /// release in a `levels` folder next to the executable - a `pack.json`
    /// manifest and one .osz per level - and its version is remembered in the
    /// ruleset's settings, so each level is imported once per pack version
    /// and never again. A level the player already has is skipped by the
    /// game's own import.
    ///
    /// No fork change: the game's <see cref="BeatmapManager"/> does the
    /// import, exactly as dropping a .osz on the window would.
    /// </summary>
    public partial class KanStarterPack : Component
    {
        public const string FOLDER = @"levels";
        public const string MANIFEST = @"pack.json";

        /// <summary>
        /// The pack's mark in a level's metadata (tools/level_pack writes it
        /// as the .osu Source): how the game knows a level came with kan!.
        /// </summary>
        public const string STARTER_SOURCE = @"kan! starter pack";

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private osu.Game.Rulesets.IRulesetConfigCache configs { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Scheduler.Add(import);
        }

        private void import()
        {
            try
            {
                string? folder = FindFolder();
                if (folder == null)
                    return;

                int version = ReadVersion(folder);
                if (version <= 0)
                    return;

                var config = configs.GetConfigFor(new KanRuleset()) as KanRulesetConfigManager;
                int imported = config?.GetBindable<int>(KanRulesetSetting.StarterPackVersion).Value ?? 0;
                if (version <= imported)
                    return;

                string[] levels = Directory.GetFiles(folder, "*.osz").OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (levels.Length == 0)
                    return;

                Logger.Log($"kan!: importing the starter pack v{version} ({levels.Length} levels)");

                // Quietly, as one batch: the levels are simply there in song
                // select. A notification that is never posted keeps the first
                // launch free of five "Imported ..." toasts (calm by design).
                var quiet = new ProgressNotification();
                beatmaps.Import(quiet, levels.Select(l => new BundledLevel(l)).ToArray(), new ImportParameters { Batch = true })
                        .ContinueWith(t =>
                        {
                            if (t.Exception != null || t.Result.Count() < levels.Length)
                            {
                                // Not remembered: the next launch tries again.
                                Logger.Log($"kan!: the starter pack imported {(t.Exception == null ? t.Result.Count() : 0)} of {levels.Length} levels: {t.Exception?.GetBaseException().Message}");
                                return;
                            }

                            Schedule(() =>
                            {
                                if (config != null)
                                    config.GetBindable<int>(KanRulesetSetting.StarterPackVersion).Value = version;
                            });
                        });
            }
            catch (Exception e)
            {
                // Never keep the game from starting over a bundle of levels.
                Logger.Log($"kan!: the starter pack did not import: {e.Message}");
            }
        }

        /// <summary>
        /// A level shipped with the release: imported like any .osz, but never
        /// deleted afterwards (the game deletes an imported .osz; this one
        /// belongs to the installed game, read-only on some systems).
        /// </summary>
        private class BundledLevel : ImportTask
        {
            public BundledLevel(string path)
                : base(path)
            {
            }

            public override void DeleteFile()
            {
            }
        }

        /// <summary>
        /// The pack's folder: next to the game's executable (how the release
        /// ships it), or next to this plugin when the ruleset was installed
        /// by hand into the rulesets folder.
        /// </summary>
        private static string? FindFolder()
        {
            foreach (string? root in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(KanStarterPack).Assembly.Location) })
            {
                if (string.IsNullOrEmpty(root))
                    continue;

                string candidate = Path.Combine(root, FOLDER);
                if (File.Exists(Path.Combine(candidate, MANIFEST)))
                    return candidate;
            }

            return null;
        }

        private static int ReadVersion(string folder)
        {
            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, MANIFEST)));
                return manifest.RootElement.TryGetProperty("version", out var version) ? version.GetInt32() : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
