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
    internal static class ForgeFormat
    {
        /// <summary>m:ss (or m:ss.s for short material).</summary>
        public static string Time(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;

            int minutes = (int)(seconds / 60);
            double rest = seconds - minutes * 60;

            return seconds < 10
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.0}", minutes, rest)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, Math.Floor(rest));
        }

        /// <summary>Seconds as the track editor parses them (invariant culture).</summary>
        public static string Seconds(double seconds) => seconds.ToString("0.00", CultureInfo.InvariantCulture);

        public static bool TryParseSeconds(string? text, out double seconds)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);

        /// <summary>The quietest level the volume controls show; at or below it a track is silent.</summary>
        public const double SILENT_DB = -60;

        /// <summary>Linear gain → dB (silence = <see cref="SILENT_DB"/>).</summary>
        public static double GainToDb(float gain) => gain <= 0.001f ? SILENT_DB : Math.Max(SILENT_DB, 20 * Math.Log10(gain));

        /// <summary>dB → linear gain, capped at +20 dB (<see cref="ForgeTrack.MAX_GAIN"/>).</summary>
        public static float DbToGain(double db) => db <= SILENT_DB ? 0 : (float)Math.Min(ForgeTrack.MAX_GAIN, Math.Pow(10, db / 20));

        /// <summary>"+6.0 dB", "0.0 dB", "silent".</summary>
        public static string Db(float gain) => gain <= 0.001f ? @"silent" : $"{20 * Math.Log10(gain):+0.0;-0.0;0.0} dB";

        public static string Plural(int n, string singular, string plural) => n == 1 ? $"{n} {singular}" : $"{n} {plural}";
    }
}
