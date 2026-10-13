// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using Kan.Ruleset.Objects;

namespace Kan.Ruleset.Beatmaps
{
    /// <summary>
    /// Phase 0 placeholder converter: every source hit object becomes a
    /// timing-only <see cref="KanHitObject"/> so any beatmap can enter the
    /// placeholder mode. The real `.kan`/`.kchart` mapping (Layer 1/2/3, see
    /// docs/architecture/osu-integration.md §3) replaces this in Phase 1.
    /// </summary>
    public class KanBeatmapConverter : BeatmapConverter<KanHitObject>
    {
        public KanBeatmapConverter(IBeatmap beatmap, osu.Game.Rulesets.Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        public override bool CanConvert() => true;

        protected override IEnumerable<KanHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            yield return new KanHitObject
            {
                StartTime = original.StartTime,
            };
        }
    }
}
