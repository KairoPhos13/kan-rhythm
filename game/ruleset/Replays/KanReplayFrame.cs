// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;

namespace Kan.Ruleset.Replays
{
    /// <summary>What a replay frame holds: one fact the judge heard.</summary>
    public enum KanFactKind
    {
        /// <summary>A note started (<see cref="KanReplayFrame.Index"/> = which heard note).</summary>
        Hit = 1,

        PitchProvisional = 2,
        PitchFinal = 3,
        UnresolvedProvisional = 4,
        UnresolvedFinal = 5,

        /// <summary>A pitch reading (the trail): <see cref="KanReplayFrame.Midi"/>, <see cref="KanReplayFrame.Level"/>.</summary>
        Reading = 6,

        /// <summary>
        /// The play's recording (<see cref="Kan.Forge.Play.PlayTake"/>): <see cref="KanReplayFrame.Index"/>
        /// is its id, so the replay finds what the player played (owner 2026-10-22).
        /// </summary>
        Take = 7,
    }

    /// <summary>
    /// A kan! replay is what the judge heard, not what was pressed (owner
    /// 2026-10-15: a replay listened to the microphone again). Each frame is
    /// one fact, stamped with the gameplay time it reached the judge; the
    /// fact's own time is <see cref="Time"/> − <see cref="DelayMs"/>. Played
    /// back in order, the same facts give the same verdicts.
    /// </summary>
    public class KanReplayFrame : ReplayFrame, IConvertibleReplayFrame
    {
        public KanFactKind Kind;

        /// <summary>Which heard note (in the order they were heard) a hit or pitch belongs to.</summary>
        public int Index;

        /// <summary>Pitch: the note's MIDI number (fractional for a reading).</summary>
        public float Midi;

        public float Cents;

        /// <summary>A reading's level (the trail's brightness).</summary>
        public float Level;

        /// <summary>How long before <see cref="Time"/> the fact happened (an onset or a reading is reported a moment later).</summary>
        public int DelayMs;

        public double FactTime => Time - DelayMs;

        public KanReplayFrame()
        {
        }

        public KanReplayFrame(double time, KanFactKind kind, int index = 0, float midi = 0, float cents = 0, float level = 0, int delayMs = 0)
            : base(time)
        {
            Kind = kind;
            Index = index;
            Midi = midi;
            Cents = cents;
            Level = level;
            DelayMs = delayMs;
        }

        // Legacy (.osr) encoding: the button field carries the kind and the delay
        // (kind * 100000 + delay), the coordinates carry the values. Coordinates
        // are written as round-trip floats and must stay within ±131072.
        private const int kind_scale = 100000;

        public LegacyReplayFrame ToLegacy(IBeatmap beatmap)
        {
            float x, y;

            switch (Kind)
            {
                case KanFactKind.Hit:
                case KanFactKind.UnresolvedProvisional:
                case KanFactKind.UnresolvedFinal:
                    x = 0;
                    y = Index;
                    break;

                case KanFactKind.PitchProvisional:
                case KanFactKind.PitchFinal:
                    // The note and its cents in one number: 72.12 = MIDI 72, +12 cents.
                    x = Midi + Cents / 100f;
                    y = Index;
                    break;

                case KanFactKind.Take:
                    // The id in two halves: each fits a coordinate exactly.
                    x = Index & 0xFFFF;
                    y = (Index >> 16) & 0xFFFF;
                    break;

                default:
                    x = Midi;
                    y = Level;
                    break;
            }

            int delay = System.Math.Clamp(DelayMs, 0, kind_scale - 1);
            return new LegacyReplayFrame(Time, x, y, (ReplayButtonState)((int)Kind * kind_scale + delay));
        }

        public void FromLegacy(LegacyReplayFrame currentFrame, IBeatmap beatmap, ReplayFrame? lastFrame = null)
        {
            Time = currentFrame.Time;
            int code = (int)currentFrame.ButtonState;
            Kind = (KanFactKind)(code / kind_scale);
            DelayMs = code % kind_scale;
            float x = currentFrame.MouseX ?? 0, y = currentFrame.MouseY ?? 0;

            switch (Kind)
            {
                case KanFactKind.Hit:
                case KanFactKind.UnresolvedProvisional:
                case KanFactKind.UnresolvedFinal:
                    Index = (int)System.MathF.Round(y);
                    break;

                case KanFactKind.PitchProvisional:
                case KanFactKind.PitchFinal:
                    Midi = System.MathF.Round(x);
                    Cents = (x - Midi) * 100f;
                    Index = (int)System.MathF.Round(y);
                    break;

                case KanFactKind.Take:
                    Index = (int)System.MathF.Round(x) | ((int)System.MathF.Round(y) << 16);
                    break;

                default:
                    Midi = x;
                    Level = y;
                    break;
            }
        }
    }
}
