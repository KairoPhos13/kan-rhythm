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

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Track durations for display (time axis, header, range editing). Read
    /// lazily off-thread from the file's headers and cached by path — a
    /// presentation-only cache, the track model is untouched.
    /// </summary>
    internal static class TrackDurations
    {
        private static readonly ConcurrentDictionary<string, double> durations = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> requested = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The duration in seconds, or null while unknown (a read is started on first ask).</summary>
        public static double? Get(string path)
        {
            if (durations.TryGetValue(path, out double seconds))
                return seconds;

            if (requested.TryAdd(path, true))
                Task.Run(() => read(path));

            return null;
        }

        private static void read(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                using var file = TagLibUtils.GetTagLibFile(path);
                double seconds = file.Properties?.Duration.TotalSeconds ?? 0;
                if (seconds > 0)
                    durations[path] = seconds;
            }
            catch
            {
                // Unknown duration: the UI simply omits the time axis.
            }
        }
    }
}
