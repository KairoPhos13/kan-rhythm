// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Audio.Effects;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The kan! playfield (gameplay MVP, owner 2026-10-14): the living staff
    /// over a background that brightens as the music blooms. The music is the
    /// reward: playing well opens the level audio up (a low-pass filter that
    /// lifts with the bloom); misses close it a little, it never stops.
    /// Levels without a score show the placeholder.
    /// </summary>
    public partial class KanPlayfield : Playfield
    {
        private static readonly Color4 dark = Color4Extensions.FromHex(@"120e1a");
        private static readonly Color4 bright = Color4Extensions.FromHex(@"2c2148");

        private const int muffled_hz = 450;

        [Cached]
        private readonly KanPlayState state;

        private Box background = null!;
        private OsuSpriteText status = null!;
        private AudioFilter? bloomFilter;

        public KanPlayfield(KanPlayState state)
        {
            this.state = state;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            RelativeSizeAxes = Axes.Both;

            if (state.Notes.Count == 0)
            {
                AddInternal(placeholder());
                return;
            }

            AddRangeInternal(new Drawable[]
            {
                state,
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = dark, Depth = float.MaxValue },
                new KanStaff(state),
                status = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Margin = new MarginPadding { Top = 90 },
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                    Colour = Color4Extensions.FromHex(@"ffb3c7"),
                },
                bloomFilter = new AudioFilter(audio.TrackMixer),
            });

            state.Status.BindValueChanged(s => status.Text = s.NewValue, true);
        }

        protected override void Update()
        {
            base.Update();

            if (bloomFilter == null) return;

            double bloom = state.Bloom;
            background.Colour = ColourInfo.GradientVertical(Interpolation.ValueAt(bloom, dark, bright, 0, 1), dark);

            // Fully open near the top of the bloom; otherwise the higher frequencies come in with it.
            bloomFilter.Cutoff = bloom >= 0.97
                ? AudioFilter.MAX_LOWPASS_CUTOFF
                : (int)(muffled_hz * Math.Pow((double)AudioFilter.MAX_LOWPASS_CUTOFF / muffled_hz, bloom));
        }

        private static Drawable placeholder() => new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Direction = FillDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Spacing = new osuTK.Vector2(0, 8),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = @"kan!",
                    Font = OsuFont.GetFont(size: 72, weight: FontWeight.Bold),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = @"This level has no score yet: write it in the editor's compose tab.",
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.Regular),
                },
            },
        };
    }
}
