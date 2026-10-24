// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Change detection for the shared set: <see cref="Order"/> changes when
    /// tracks are added/removed/reordered (rows rebuild); <see cref="State"/>
    /// when roles, flags or analysis results change (rows refresh in place).
    /// </summary>
    internal readonly record struct SetSignature(string Order, string State)
    {
        public static SetSignature Of(ForgeTrackSet set, Func<ForgeTrack, string>? extra = null)
        {
            // The demux worker adds stems from its own thread: snapshot first
            // (List.ToArray never throws on concurrent modification).
            ForgeTrack[] tracks;

            try
            {
                tracks = set.Tracks.ToArray();
            }
            catch
            {
                return new SetSignature(@"?", @"?");
            }

            var order = new StringBuilder();
            var state = new StringBuilder();

            state.Append('S').Append(Math.Round(set.AudioStartMs)).Append('-').Append(Math.Round(set.AudioEndMs)).Append('|');

            foreach (var t in tracks)
            {
                if (t == null) continue;

                order.Append(RuntimeHelpers.GetHashCode(t)).Append(',');
                state.Append(t.IsReference ? 'R' : '-')
                     .Append(t.IsCompanion ? 'C' : '-')
                     .Append(t.DemuxPending ? 'D' : '-')
                     .Append(t.IsSeparated ? 's' : '-')
                     .Append(t.IsMixdown ? 'x' : '-')
                     .Append(t.Muted ? 'm' : '-')
                     .Append(t.LikelyEmpty ? 'e' : '-')
                     .Append(t.Peaks != null ? 'p' : '-')
                     .Append(t.AnalysisFailed ? 'f' : '-')
                     .Append(t.Gain.ToString("0.00", CultureInfo.InvariantCulture))
                     .Append('@').Append(Math.Round(t.OffsetMs))
                     .Append('[').Append(Math.Round(t.ClipStartMs)).Append(',').Append(Math.Round(t.ClipEndMs)).Append(']')
                     .Append(editsKey(t.Edits))
                     .Append(t.Name)
                     .Append(extra?.Invoke(t))
                     .Append('|');
            }

            return new SetSignature(order.ToString(), state.ToString());
        }

        private static string editsKey(TrackEdits e)
        {
            if (e.IsEmpty) return string.Empty;

            var sb = new StringBuilder(@"[");
            foreach (var r in e.Ranges)
                sb.Append((int)r.Mode).Append(':').Append(Math.Round(r.FromMs)).Append('-').Append(Math.Round(r.ToMs)).Append(';');
            return sb.Append(Math.Round(e.LowCutHz)).Append('/').Append(Math.Round(e.HighCutHz)).Append('^').Append(e.PitchSemitones).Append(']').ToString();
        }

        /// <summary>Everything that is saved with the set, as one comparable string.</summary>
        public string Full => Order + @"#" + State;
    }
}
