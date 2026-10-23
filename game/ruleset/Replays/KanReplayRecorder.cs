// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.Replays
{
    /// <summary>
    /// Records a play's replay. Nothing is pressed in kan!, so no input frame
    /// is ever recorded; the play state adds what the judge heard instead
    /// (<see cref="Add"/>).
    /// </summary>
    public partial class KanReplayRecorder : ReplayRecorder<KanAction>
    {
        private readonly Replay replay;

        public KanReplayRecorder(Score target)
            : base(target)
        {
            Target = target;
            replay = target.Replay;
        }

        /// <summary>The play being recorded (its score, once it ends).</summary>
        public Score Target { get; }

        public void Add(KanReplayFrame frame) => replay.Frames.Add(frame);

        protected override ReplayFrame? HandleFrame(Vector2 mousePosition, List<KanAction> actions, ReplayFrame previousFrame) => null;
    }

    /// <summary>
    /// Marks the play as a replay (no microphone). The facts themselves are
    /// read by the play state straight from the replay's frames.
    /// </summary>
    public class KanReplayInputHandler : FramedReplayInputHandler<KanReplayFrame>
    {
        public KanReplayInputHandler(Replay replay)
            : base(replay)
        {
        }
    }
}
