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
    /// <summary>An editable seconds range, persisted across row rebuilds; the waveform handles and the typed boxes share it.</summary>
    internal class EditRange
    {
        /// <summary>Range start in seconds (&lt; 0: unset).</summary>
        public readonly BindableDouble From = new BindableDouble(-1);

        /// <summary>Range end in seconds (&lt; 0: unset).</summary>
        public readonly BindableDouble To = new BindableDouble(-1);

        public readonly Bindable<string> FromText = new Bindable<string>(string.Empty);
        public readonly Bindable<string> ToText = new Bindable<string>(string.Empty);

        private bool syncing;

        public EditRange()
        {
            link(From, FromText);
            link(To, ToText);
        }

        public bool IsSet => From.Value >= 0 && To.Value > From.Value;

        private void link(BindableDouble value, Bindable<string> text)
        {
            value.BindValueChanged(v => sync(() => text.Value = v.NewValue < 0 ? string.Empty : ForgeFormat.Seconds(v.NewValue)));
            text.BindValueChanged(v => sync(() => value.Value = ForgeFormat.TryParseSeconds(v.NewValue, out double s) && s >= 0 ? s : -1));
        }

        private void sync(Action apply)
        {
            if (syncing) return;

            syncing = true;
            apply();
            syncing = false;
        }
    }
}
