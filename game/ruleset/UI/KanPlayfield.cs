// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.UI;
using osuTK;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Phase 0 placeholder screen for the kan! ruleset: proves that the
    /// ruleset plugin loads and its playable mode can be entered. No gameplay
    /// content — see game/TODO.md for the real highway/judgment work.
    /// </summary>
    public partial class KanPlayfield : Playfield
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            AddInternal(new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, 8),
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
                        Text = KanRuleset.TAGLINE,
                        Font = OsuFont.GetFont(size: 28, weight: FontWeight.Light),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = @"placeholder mode — real-instrument judgment core arrives in Phase 1",
                        Font = OsuFont.GetFont(size: 16, weight: FontWeight.Regular),
                    },
                },
            });
        }
    }
}
