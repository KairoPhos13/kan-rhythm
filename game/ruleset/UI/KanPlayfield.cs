// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Audio.Effects;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.UI;
using Kan.Forge.Play;
using osuTK.Graphics;
using Kan.Core.Play;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The kan! playfield (gameplay MVP, owner 2026-10-14): the living staff
    /// over a background that brightens as the music blooms. The music is the
    /// reward: playing well opens the level audio up (a low-pass filter that
    /// lifts with the bloom). A miss is heard at once (owner 2026-10-15: "when
    /// you fail at osu the song starts sounding horrible"): the music muffles
    /// hard and drops in volume for a moment, then recovers to the bloom.
    /// Levels without a score show the placeholder.
    /// </summary>
    public partial class KanPlayfield : Playfield
    {
        private const int muffled_hz = 450;

        [Cached]
        private readonly KanPlayState state;

        private Box background = null!;
        private Box climaxGlow = null!;
        private KanScene scene = null!;

        /// <summary>The living staff (null for a level without a score).</summary>
        public KanStaff? Staff { get; private set; }

        private Container? scaled;

        /// <summary>How much bigger than its design size the staff draws (screen height × the note size setting).</summary>
        public float UiScale { get; private set; } = 1;

        /// <summary>The staff is designed for a screen this high (px).</summary>
        private const float design_height = 900;

        // Cached by its own type (DrawableRulesetDependencies), not as the interface.
        [Resolved(CanBeNull = true)]
        private Kan.Ruleset.Configuration.KanRulesetConfigManager? config { get; set; }

        // osu!'s own UI scale (Graphics → UI scaling): the playfield is left out
        // of it (the fork's Player.createGameplayComponents opts gameplay out of
        // UI scaling), so kan!'s text applies it here (issue 19).
        [Resolved(CanBeNull = true)]
        private osu.Game.Configuration.OsuConfigManager? osuConfig { get; set; }

        private osu.Framework.Bindables.Bindable<double>? noteSize;
        private osu.Framework.Bindables.Bindable<KanNoteNames>? notationNames;
        private osu.Framework.Bindables.Bindable<bool>? notationColours;
        private osu.Framework.Bindables.Bindable<Kan.Core.Design.KanPaletteMode>? notationPalette;
        private osu.Framework.Bindables.Bindable<double>? textSize;

        /// <summary>The companion's low end (beats), smoothed: the climax glow pulses with it.</summary>
        private float lowEnd;
        private OsuSpriteText status = null!;
        private AudioFilter? bloomFilter;

        /// <summary>A miss just happened: 1, fading to 0 over <see cref="miss_recovery_ms"/>.</summary>
        private double missDip;

        private const double miss_recovery_ms = 900;

        private readonly BindableDouble missVolume = new BindableDouble(1);

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        /// <summary>The level has a background picture or video to show behind the staff.</summary>
        private bool hasPicture;

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
                new KanHitchMonitor(state),
                scene = new KanScene(state),
                background = new Box { RelativeSizeAxes = Axes.Both, Depth = float.MaxValue },
                climaxGlow = new Box
                {
                    // From the bottom: the colour rises behind the staff, it does not tint it all.
                    RelativeSizeAxes = Axes.Both,
                    Height = 0.6f,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Depth = float.MaxValue - 1,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
                // Everything the player reads, scaled with the screen and the note size setting.
                scaled = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new KanInterlude(scene, state, () => beatmap.Value.TrackLoaded ? beatmap.Value.Track : null),
                        Staff = new KanStaff(state, scene),
                        new KanSectionBanner(scene),
                    },
                },
                new KanPerfectChime(state),
                status = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Margin = new MarginPadding { Top = 90 },
                    Font = KanTextScale.GameplayFont(18, FontWeight.SemiBold),
                    Colour = Color4Extensions.FromHex(@"ffb3c7"),
                },
                bloomFilter = new AudioFilter(audio.TrackMixer),
            });

            // The notation the player chose (names, colours, palette), the text
            // size and osu!'s UI scale, all followed live.
            if (config is { } kanConfig)
            {
                notationNames = kanConfig.GetBindable<KanNoteNames>(Kan.Ruleset.Configuration.KanRulesetSetting.NoteNames);
                notationColours = kanConfig.GetBindable<bool>(Kan.Ruleset.Configuration.KanRulesetSetting.ColourByNote);
                notationPalette = kanConfig.GetBindable<Kan.Core.Design.KanPaletteMode>(Kan.Ruleset.Configuration.KanRulesetSetting.Palette);
                textSize = kanConfig.GetBindable<double>(Kan.Ruleset.Configuration.KanRulesetSetting.TextSize);
                notationNames.BindValueChanged(v => KanNotation.Names = v.NewValue, true);
                notationColours.BindValueChanged(v => KanNotation.ColourByNote = v.NewValue, true);
                notationPalette.BindValueChanged(v => KanNotation.Palette = v.NewValue, true);
                textSize.BindValueChanged(v => KanTextScale.Factor = (float)v.NewValue, true);
            }

            osuConfig?.GetBindable<float>(osu.Game.Configuration.OsuSetting.UIScale)
                     .BindValueChanged(v => KanTextScale.OsuUiScale = v.NewValue, true);

            var metadata = beatmap.Value.Metadata;
            hasPicture = !string.IsNullOrEmpty(metadata.BackgroundFile)
                         || beatmap.Value.Storyboard.Layers.Any(l => l.Elements.Any(e => e is osu.Game.Storyboards.StoryboardVideo));

            state.Status.BindValueChanged(s => status.Text = s.NewValue, true);
            state.Judged += onJudged;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            beatmap.Value.Track.AddAdjustment(AdjustableProperty.Volume, missVolume);
        }

        private void onJudged(int index, LiveVerdict verdict)
        {
            // Wait mode is a gentle walk: a miss is felt, softly.
            if (verdict.Result == LiveResult.Miss) missDip = state.WaitForPlayer ? 0.45 : 1;
        }

        protected override void Update()
        {
            base.Update();

            // On a bigger screen everything grows with it (owner 2026-10-15: "on a 2K screen I can barely see it").
            if (scaled != null)
            {
                noteSize ??= config?.GetBindable<double>(Kan.Ruleset.Configuration.KanRulesetSetting.NoteSize);
                UiScale = (float)(Math.Max(0.75, DrawHeight / design_height) * (noteSize?.Value ?? 1.2));
                scaled.Scale = new osuTK.Vector2(UiScale);
                scaled.Size = new osuTK.Vector2(1 / UiScale);
            }

            if (bloomFilter == null) return;

            double bloom = state.Bloom;

            // The level's palette; playing well lights it up (as much as the design's glow says).
            double lit = Math.Clamp(bloom * scene.Design.Glow, 0, 1);
            background.Colour = ColourInfo.GradientVertical(Interpolation.ValueAt(lit, scene.BackgroundDark, scene.BackgroundLit, 0, 1), scene.BackgroundDark);

            // A level with a picture (or video): the palette becomes a veil over it,
            // thinner as the player plays well and in rests.
            if (hasPicture)
            {
                float shown = Math.Clamp(scene.Design.Picture + 0.2f * (float)lit + 0.25f * scene.Rest, 0, 1);
                background.Alpha = 1 - shown;
            }

            // A climax glows from below in its colour, pulsing with the music's low end.
            if (scene.ClimaxAmount > 0.001f && beatmap.Value.TrackLoaded)
            {
                var amplitudes = beatmap.Value.Track.CurrentAmplitudes.FrequencyAmplitudes.Span;
                float low = 0;
                for (int i = 1; i < Math.Min(8, amplitudes.Length); i++) low += amplitudes[i];
                low = Math.Min(1, low / 7 * 2.5f);
                lowEnd = low > lowEnd ? low : lowEnd - (lowEnd - low) * (float)Math.Min(1, Math.Abs(Time.Elapsed) / 180);
            }

            climaxGlow.Colour = ColourInfo.GradientVertical(scene.Climax.Opacity(0), scene.Climax);
            climaxGlow.Alpha = scene.ClimaxAmount * (0.06f + 0.22f * lowEnd) * Math.Min(1, scene.Design.Glow);

            missDip = Math.Max(0, missDip - Math.Abs(Time.Elapsed) / miss_recovery_ms);
            double dip = missDip * missDip; // sharp at once, easing back

            // Fully open near the top of the bloom; otherwise the higher frequencies come in with it.
            // A miss pulls it down hard (towards 200 Hz) and ducks the volume.
            double open = bloom >= 0.97 && dip == 0 ? 1 : Math.Max(0, bloom - dip * 1.2);
            bloomFilter.Cutoff = open >= 1
                ? AudioFilter.MAX_LOWPASS_CUTOFF
                : (int)Math.Max(200, muffled_hz * Math.Pow((double)AudioFilter.MAX_LOWPASS_CUTOFF / muffled_hz, open));
            missVolume.Value = 1 - 0.45 * dip;
        }

        protected override void Dispose(bool isDisposing)
        {
            state.Judged -= onJudged;
            if (beatmap.Value?.TrackLoaded == true)
                beatmap.Value.Track.RemoveAdjustment(AdjustableProperty.Volume, missVolume);
            base.Dispose(isDisposing);
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
                    Font = KanTextScale.GameplayFont(72, FontWeight.Bold),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = @"This level has no score yet: write it in the editor's compose tab.",
                    Font = KanTextScale.GameplayFont(18, FontWeight.Regular),
                },
            },
        };
    }
}
