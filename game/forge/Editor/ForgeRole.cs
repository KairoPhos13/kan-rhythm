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
    /// <summary>The four roles a track can take (presentation mirror of <see cref="ForgeTrack"/>'s flags).</summary>
    internal enum ForgeRole
    {
        Companion,
        Reference,
    }

    /// <summary>
    /// The role vocabulary (owner naming 2026-10-14): companion tracks play
    /// under the player and their mix is the level audio; the reference is
    /// the full performance (transcription, future demo playback).
    /// </summary>
    internal static class ForgeRoles
    {
        public static readonly ForgeRole[] ALL = { ForgeRole.Companion, ForgeRole.Reference };

        public static string Label(ForgeRole role) => role switch
        {
            ForgeRole.Companion => @"companion",
            _ => @"reference",
        };

        public static IconUsage Icon(ForgeRole role) => role switch
        {
            ForgeRole.Companion => FontAwesome.Solid.LayerGroup,
            _ => FontAwesome.Solid.Microphone,
        };

        public static Color4 Colour(OsuColour colours, ForgeRole role) => role switch
        {
            ForgeRole.Companion => colours.Blue,
            _ => colours.Yellow,
        };

        /// <summary>The demux action's colour (an action, not a role).</summary>
        public static Color4 DemuxColour(OsuColour colours) => colours.PurpleLight;

        public static string Description(ForgeRole role) => role switch
        {
            ForgeRole.Companion => @"companion: plays under the player during the level. The companion tracks, mixed, are the level audio.",
            _ => @"reference, the full performance (one per level): what gets transcribed, and what the demo plays along with the companion.",
        };

        public static bool Has(ForgeTrack track, ForgeRole role) => role switch
        {
            ForgeRole.Companion => track.IsCompanion,
            _ => track.IsReference,
        };

        /// <summary>The role that colours a selected track's highlight (first active, in role order).</summary>
        public static ForgeRole? Primary(ForgeTrack track)
        {
            foreach (var role in ALL)
            {
                if (Has(track, role))
                    return role;
            }

            return null;
        }
    }
}
