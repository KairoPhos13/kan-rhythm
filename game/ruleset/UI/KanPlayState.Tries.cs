// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Replays;
using osu.Game.Screens.Play;
using Kan.Core.Native;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;
using Kan.Core.Play;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Tries at a note: the notes around it (its neighbours, the notes a wrong pitch may belong to)
    /// and the wrong notes played at it.
    /// </summary>
    public partial class KanPlayState : Component
    {
        /// <summary>
        /// Wait mode: notes played wrong before right are a miss at once, when the
        /// wrong note is confirmed (owner 2026-10-15: they break the combo;
        /// 2026-10-17: at once, not when the right note is finally found).
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, LiveVerdict> wrongTry = new System.Collections.Generic.Dictionary<int, LiveVerdict>();

        private readonly System.Collections.Generic.Dictionary<int, int[]> expectedAround = new System.Collections.Generic.Dictionary<int, int[]>();

        /// <summary>
        /// The notes the score has around a note: the previous one, a chord's
        /// others, the next ones within the window; played, they are not wrong
        /// tries at it (<see cref="LiveJudge.IsWrongTry"/>).
        /// </summary>
        public IReadOnlyCollection<int> ExpectedAround(KanHitObject n)
        {
            if (expectedAround.TryGetValue(n.Index, out int[]? known)) return known;

            var around = new System.Collections.Generic.List<int>();
            if (n.Index > 0 && !Notes[n.Index - 1].PitchUnknown) around.Add(Notes[n.Index - 1].Midi);

            double reach = LiveJudge.WindowMs(LiveResult.Meh, n.PaceMs) * Judge.WindowScale;

            for (int i = n.Index - 1; i >= 0 && Notes[i].StartTime >= n.StartTime - reach; i--)
                if (!Notes[i].PitchUnknown) around.Add(Notes[i].Midi);

            for (int i = n.Index + 1; i < Notes.Count && Notes[i].StartTime <= n.StartTime + reach; i++)
                if (!Notes[i].PitchUnknown) around.Add(Notes[i].Midi);

            return expectedAround[n.Index] = around.Distinct().ToArray();
        }

        /// <summary>When the score's previous note (not a chord mate) starts: the judge's window reaches back at most halfway to it.</summary>
        public double PreviousStart(KanHitObject n)
        {
            for (int i = n.Index - 1; i >= 0; i--)
                if (Notes[i].StartTime < n.StartTime) return Notes[i].StartTime;

            return double.NegativeInfinity;
        }

        /// <summary>When the score's next note (not a chord mate) starts: the judge's window reaches on at most halfway to it.</summary>
        public double NextStart(KanHitObject n)
        {
            for (int i = n.Index + 1; i < Notes.Count; i++)
                if (Notes[i].StartTime > n.StartTime) return Notes[i].StartTime;

            return double.PositiveInfinity;
        }

        /// <summary>Whether the judge heard a note played for <paramref name="n"/>, within its window.</summary>
        private bool heard(KanHitObject n) => Judge.HasHeard(n.StartTime, n.Midi, n.PitchUnknown, n.PaceMs, PreviousStart(n), NextStart(n));

        /// <summary>A wrong try heard for a note (wait mode): the drawable applies it at once.</summary>
        public LiveVerdict? WrongTry(KanHitObject note) => wrongTry.TryGetValue(note.Index, out var v) ? v : null;

        private void noteWrongTry(KanHitObject n, int played, double atMs)
        {
            if (!wrongTry.TryAdd(n.Index, new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, atMs - n.StartTime, played, 0))) return;

            WrongWhileWaiting?.Invoke(played);
        }

        /// <summary>The last attack heard (gameplay ms) and the wrong pitch held since it (readings in a row).</summary>
        private double lastOnsetMs = double.NegativeInfinity;

        private int wrongHeldMidi, wrongHeldReadings;

        /// <summary>
        /// Wait mode: a wrong pitch held since an attack made for the note is a
        /// wrong try, without waiting for libkan's final pitch; that comes
        /// when the note ends, i.e. when the right note is found.
        /// </summary>
        private void trackWrongPitch(KanHitObject n, KanReplayFrame f)
        {
            int played = f.Midi > 0 ? (int)Math.Round(f.Midi) : 0;

            if (n.PitchUnknown || lastOnsetMs < n.StartTime - LiveJudge.TRY_BEFORE_MS || LiveJudge.Accepts(f.Midi, n.Midi)
                || !LiveJudge.IsWrongTry(played, n.Midi, ExpectedAround(n)))
            {
                wrongHeldReadings = 0;
                return;
            }

            wrongHeldReadings = played == wrongHeldMidi ? wrongHeldReadings + 1 : 1;
            wrongHeldMidi = played;

            if (wrongHeldReadings >= LiveJudge.WRONG_HOLD_READINGS)
                noteWrongTry(n, played, lastOnsetMs);
        }

        /// <summary>When each heard note started (gameplay ms): a pitch counts as a try only for a note started for it.</summary>
        private readonly System.Collections.Generic.Dictionary<int, double> onsetOf = new System.Collections.Generic.Dictionary<int, double>();

        /// <summary>Whether a heard note started late enough to be a try at <paramref name="n"/> (not the previous note still sounding).</summary>
        private bool isTryAt(KanReplayFrame pitch, KanHitObject n)
            => onsetOf.TryGetValue(pitch.Index, out double onset) && onset >= n.StartTime - LiveJudge.TRY_BEFORE_MS;
    }
}
