// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using Realms;
using Kan.Core.Score;
using Kan.Forge.Editor;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// Gives levels back the length the game shows for them. A kan! level's
    /// notes live in its score (kan.score.json), and its osu! file carries
    /// the score's marks so the game and the server can read the level's
    /// length and object count from it (game/core/Score/LevelMarks.cs,
    /// docs/architecture/level-metadata.md). Levels made before that was true
    /// were imported with an empty [HitObjects] section and read as 00:00 in
    /// song select: this gives them their marks and reprocesses them, once,
    /// quietly in the background (kan!forge keeps the marks of the levels
    /// made from now on; the star ratings of all levels are recomputed by the
    /// game when KanDifficultyCalculator.Version moves on).
    ///
    /// It only looks at levels whose length is unknown (0), which is exactly
    /// the levels it can help: after one pass every level has its length and
    /// the check at startup costs one query. A level whose score has no notes
    /// keeps whatever its .osu carries (a level being built from an imported
    /// song may still rely on its original objects).
    /// </summary>
    public partial class KanMarksMigration : Component
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Task.Factory.StartNew(migrate, TaskCreationOptions.LongRunning).ContinueWith(t =>
            {
                if (t.Exception != null)
                    Logger.Log($"kan!: giving the levels their marks back did not finish: {t.Exception.GetBaseException().Message}");
            });
        }

        private void migrate()
        {
            // Detached models: what the working beatmaps and the save below
            // work with. The query is the gate: only levels whose length is
            // unknown are considered (Realm cannot express "has a score
            // file", so that is checked in memory next).
            List<BeatmapInfo> candidates = realm.Run(r => r.All<BeatmapInfo>()
                                                           .Filter(@"BeatmapSet.DeletePending == false")
                                                           .Where(b => b.Length <= 0 && b.BeatmapSet != null)
                                                           .AsEnumerable()
                                                           .Select(b => b.Detach())
                                                           .ToList());

            int repaired = 0;

            foreach (var info in candidates)
            {
                try
                {
                    var working = beatmaps.GetWorkingBeatmap(info);

                    if (working.Beatmap == null || working.BeatmapInfo.BeatmapSet is not BeatmapSetInfo set)
                        continue;

                    if (set.GetFile(KanScore.FILE_NAME) == null)
                        continue;

                    if (KanLevelFiles.LoadScore(working) is not { Notes.Count: > 0 } score)
                        continue;

                    if (LevelOsuMarks.Matches(working.Beatmap, score.Notes))
                        continue;

                    if (LevelOsuMarks.Sync(working, beatmaps, score) is string error)
                        Logger.Log($"kan!: {set.GetDisplayString()} could not get its marks: {error}");
                    else
                        repaired++;
                }
                catch (Exception e)
                {
                    Logger.Log($"kan!: a level could not get its marks: {e.Message}");
                }
            }

            if (repaired > 0)
                Logger.Log($"kan!: {repaired} level(s) got their length back");
        }
    }
}
