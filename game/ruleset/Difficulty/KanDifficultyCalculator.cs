// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using Kan.Core.Score;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.Difficulty
{
    /// <summary>
    /// The star rating of a kan! level: the pace of its notes (notes per
    /// second) and how far its melody reaches (pitch span), the honest model
    /// of <see cref="LevelDifficulty"/> (game/core/Score/LevelDifficulty.cs,
    /// documented in docs/architecture/level-metadata.md). Whatever plays the
    /// notes is treated the same: nothing here is one instrument's special
    /// case.
    ///
    /// The notes come from the level's score, read through the working
    /// beatmap (so the stars are right even before the game's storage is
    /// known), falling back to the converted objects. A level without a
    /// score keeps a timing-only estimate: its objects' pace, no reach.
    ///
    /// This is the model of the moment, not the final one (the `kp`-era
    /// difficulty work, docs/adr/0012, is a later milestone). It computes no
    /// skills and no performance attributes yet; see
    /// docs/architecture/level-metadata.md for what that means for display.
    /// </summary>
    public class KanDifficultyCalculator : DifficultyCalculator
    {
        /// <summary>
        /// A date-stamped version of this calculation, discerned from older
        /// ones by the game (DifficultyCalculator.Version): bump it whenever
        /// the formula or the notes behind it change, and every kan! level a
        /// player already has gets its star rating recomputed at startup
        /// (BackgroundDataStoreProcessor.clearOutdatedStarRatings).
        /// </summary>
        public override int Version => 20261112;

        public KanDifficultyCalculator(osu.Game.Rulesets.IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            var difficulty = LevelDifficulty.Of(loadScoreNotes() ?? notesOf(beatmap.HitObjects));
            return new DifficultyAttributes(mods, difficulty.Stars) { MaxCombo = difficulty.NoteCount };
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods)
            => Enumerable.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
            => Array.Empty<Skill>();

        /// <summary>The level's score notes, or null when the level has no score.</summary>
        private IReadOnlyList<ScoreNote>? loadScoreNotes()
            => KanLevelFiles.LoadScore(WorkingBeatmap) is { Notes.Count: > 0 } score ? score.Notes : null;

        /// <summary>The converted objects as notes (levels without a score: pacing only, pitch unknown).</summary>
        private static IReadOnlyList<ScoreNote> notesOf(IReadOnlyList<osu.Game.Rulesets.Objects.HitObject> objects)
            => objects.OfType<KanHitObject>().Select(h => new ScoreNote
            {
                StartMs = h.StartTime,
                DurationMs = h.DurationMs,
                Midi = h.Midi,
                Cents = h.Cents,
                PitchUnknown = !h.HasScore || h.PitchUnknown,
            }).ToList();
    }
}
