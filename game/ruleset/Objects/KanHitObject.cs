// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

namespace Kan.Ruleset.Objects
{
    /// <summary>
    /// Placeholder hit object for the Phase 0 skeleton. Carries only a start
    /// time; the real canonical-score event model (articulations, technique
    /// payloads — see docs/architecture/osu-integration.md §3) arrives in
    /// Phase 1 with the judgment core.
    /// </summary>
    public class KanHitObject : osu.Game.Rulesets.Objects.HitObject
    {
    }
}
