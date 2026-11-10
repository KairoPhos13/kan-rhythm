// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Solo: "hear only this track" (Ctrl+click adds it to the tracks
    /// already soloed). A listening aid only: rules in <see cref="SetMonitor"/>.
    /// </summary>
    internal partial class SoloButton : IconButton
    {
        private readonly ForgeTrack track;
        private readonly Action onChanged;
        private bool additive;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public SoloButton(ForgeTrack track, Action onChanged)
        {
            this.track = track;
            this.onChanged = onChanged;

            Icon = FontAwesome.Solid.Headphones;
            Size = new Vector2(28);
            Action = () =>
            {
                monitor.Solo(track, additive);
                onChanged();
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            additive = e.ControlPressed;
            return base.OnClick(e);
        }

        protected override void Update()
        {
            base.Update();

            bool on = monitor.IsSoloed(track);
            IconColour = on ? colours.Yellow : colourProvider.Content2;
            TooltipText = on
                ? @"soloed: you hear only the soloed tracks (click to stop)"
                : monitor.AnySoloed
                    ? @"solo: hear only this track (Ctrl+click: add it to the solo)"
                    : @"solo: hear only this track (Ctrl+click several tracks to hear them together)";
        }
    }

    /// <summary>Mute: leave the track out of the mix (the level audio too). Saved and undoable.</summary>
    internal partial class MuteButton : IconButton
    {
        private readonly ForgeTrack track;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        public MuteButton(ForgeTrack track, Action onChanged)
        {
            this.track = track;

            Size = new Vector2(28);
            Action = () =>
            {
                monitor.Mute(track);
                onChanged();
            };
        }

        protected override void Update()
        {
            base.Update();

            Icon = track.Muted ? FontAwesome.Solid.VolumeMute : FontAwesome.Solid.VolumeUp;
            IconColour = track.Muted ? colours.Orange1 : colourProvider.Content2;
            TooltipText = track.Muted
                ? @"muted: left out of the mix and of the level audio (click to unmute)"
                : @"mute: leave this track out of the mix and of the level audio";
        }
    }

    /// <summary>One short word saying why a track is silent (empty while it is heard).</summary>
    internal partial class HearingLabel : OsuSpriteText
    {
        private readonly ForgeTrack track;

        [Resolved]
        private SetMonitor monitor { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public HearingLabel(ForgeTrack track)
        {
            this.track = track;
            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold);
        }

        protected override void Update()
        {
            base.Update();

            switch (monitor.HearingOf(track))
            {
                case Hearing.Muted:
                    Text = @"muted";
                    Colour = colours.Orange1;
                    break;

                case Hearing.SilencedBySolo:
                    Text = @"silent: solo";
                    Colour = colourProvider.Foreground1;
                    break;

                case Hearing.NotInLevelAudio:
                    Text = @"not in level audio";
                    Colour = colourProvider.Foreground1;
                    break;

                case Hearing.Soloed:
                    Text = @"solo";
                    Colour = colours.Yellow;
                    break;

                default:
                    Text = string.Empty;
                    break;
            }
        }
    }
}
