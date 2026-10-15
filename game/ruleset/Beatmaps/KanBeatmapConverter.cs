// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using Kan.Forge.Score;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// A level plays its score (kan.score.json, made in the compose tab): one
    /// <see cref="KanHitObject"/> per note. A level without a score keeps the
    /// timing-only placeholder conversion (each source object becomes an
    /// object that resolves by itself), so any beatmap can still be entered.
    /// </summary>
    public class KanBeatmapConverter : BeatmapConverter<KanHitObject>
    {
        private readonly KanScore? score;

        public KanBeatmapConverter(IBeatmap beatmap, osu.Game.Rulesets.Ruleset ruleset)
            : base(beatmap, ruleset)
        {
            score = KanLevelFiles.LoadScore(beatmap.BeatmapInfo.BeatmapSet);
            if (score != null && score.Notes.Count == 0) score = null;
        }

        public override bool CanConvert() => true;

        protected override IEnumerable<KanHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            yield return new KanHitObject { StartTime = original.StartTime };
        }

        /// <summary>The score replaces the source's objects (a level made in kan! may have none).</summary>
        protected override Beatmap<KanHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
        {
            var beatmap = base.ConvertBeatmap(original, cancellationToken);
            if (score == null) return beatmap;

            var notes = score.Notes.OrderBy(n => n.StartMs).ThenBy(n => n.Midi).ToList();
            beatmap.HitObjects = notes.Select((n, i) => new KanHitObject
            {
                HasScore = true,
                Index = i,
                StartTime = n.StartMs,
                DurationMs = n.DurationMs,
                Midi = n.Midi,
                Cents = n.Cents,
                Intensity = n.Intensity,
                Articulation = n.Articulation,
                Vibrato = n.Vibrato,
                PitchUnknown = n.PitchUnknown,
            }).ToList();

            return beatmap;
        }
    }
}
