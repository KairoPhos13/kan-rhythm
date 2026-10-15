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
    // ----------------------------------------------------------------------
    // Shared visual vocabulary of the kan! creation surfaces (Resources and
    // the track tab). One set of colours, icons, chips and buttons so both
    // surfaces — and the stock editor around them — read as one program.
    // Pure presentation: nothing here changes the track model.
    // ----------------------------------------------------------------------

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
            ForgeRole.Companion => @"companion — plays under the player during the level. The companion tracks, mixed, are the level audio.",
            _ => @"reference — the full performance (one per level): what gets transcribed, and what the demo plays along with the companion.",
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
            return sb.Append(Math.Round(e.LowCutHz)).Append('/').Append(Math.Round(e.HighCutHz)).Append(']').ToString();
        }

        /// <summary>Everything that is saved with the set, as one comparable string.</summary>
        public string Full => Order + @"#" + State;
    }

    /// <summary>Posts the creation surfaces' messages to the game's notification overlay (history kept there).</summary>
    internal static class ForgeNotify
    {
        public static void Info(INotificationOverlay? overlay, string text, IconUsage icon)
            => overlay?.Post(new SimpleNotification { Text = text, Icon = icon });

        public static void Error(INotificationOverlay? overlay, string text)
            => overlay?.Post(new SimpleErrorNotification { Text = text });
    }

    /// <summary>
    /// A role chip: icon + label in the role's colour. Filled when the role
    /// is active, outlined when not, faded when unavailable. The long role
    /// explanations live in the tooltip, not on screen.
    /// </summary>
    internal partial class RoleChip : OsuClickableContainer
    {
        public readonly ForgeRole Role;

        private readonly bool compact;

        private Box background = null!;
        private SpriteIcon icon = null!;
        private OsuSpriteText label = null!;

        private Color4 roleColour;
        private bool active;
        private bool available = true;
        private string? unavailableReason;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        /// <param name="role">The role shown.</param>
        /// <param name="compact">Small read-only badge (explorer rows, legends).</param>
        public RoleChip(ForgeRole role, bool compact = false)
        {
            Role = role;
            this.compact = compact;

            // Before load: OsuClickableContainer sizes its content from
            // AutoSizeAxes in its own load, which runs before ours.
            AutoSizeAxes = Axes.X;
            Height = compact ? 18 : 26;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            roleColour = ForgeRoles.Colour(colours, Role);

            Masking = true;
            CornerRadius = Height / 2;
            BorderThickness = compact ? 0 : 1.5f;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(compact ? 4 : 6, 0),
                    Padding = new MarginPadding { Horizontal = compact ? 7 : 11 },
                    Children = new Drawable[]
                    {
                        icon = new SpriteIcon
                        {
                            Icon = ForgeRoles.Icon(Role),
                            Size = new Vector2(compact ? 9 : 11),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        label = new OsuSpriteText
                        {
                            Text = ForgeRoles.Label(Role),
                            Font = OsuFont.GetFont(size: compact ? 11 : 13, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                },
            };

            if (compact)
                Enabled.Value = false;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            updateLook(false);
            FinishTransforms(true);
        }

        public override LocalisableString TooltipText
        {
            get => available || unavailableReason == null ? ForgeRoles.Description(Role) : unavailableReason;
            set { }
        }

        /// <summary>Reflect the model: active (filled) and available (or faded with a reason).</summary>
        public void SetState(bool isActive, bool isAvailable = true, string? reason = null)
        {
            bool changed = isActive != active;

            active = isActive;
            available = isAvailable;
            unavailableReason = reason;

            if (LoadState >= LoadState.Ready)
                updateLook(changed);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateLook(false);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateLook(false);
            base.OnHoverLost(e);
        }

        private void updateLook(bool pulse)
        {
            bool hovered = IsHovered && !compact && available;

            Color4 fill = active ? roleColour : roleColour.Opacity(hovered ? 0.28f : 0.1f);
            Color4 ink = active ? colourProvider.Background6 : roleColour;

            background.FadeColour(fill, 150, Easing.OutQuint);
            icon.FadeColour(ink, 150, Easing.OutQuint);
            label.FadeColour(ink, 150, Easing.OutQuint);
            this.TransformTo(nameof(BorderColour), (osu.Framework.Graphics.Colour.ColourInfo)roleColour.Opacity(active ? 1 : 0.55f), 150, Easing.OutQuint);
            this.FadeTo(available ? 1 : 0.35f, 150);

            if (pulse && active)
                this.ScaleTo(1.08f, 80, Easing.OutQuint).Then().ScaleTo(1, 250, Easing.OutElasticHalf);
        }
    }

    /// <summary>A small text pill for a track's material and status tags (separated, mixdown, from video, empty?).</summary>
    internal partial class TagPill : CompositeDrawable, osu.Framework.Graphics.Cursor.IHasTooltip
    {
        public LocalisableString TooltipText { get; }

        public TagPill(string text, IconUsage? icon, Color4 colour, string tooltip)
        {
            TooltipText = tooltip;
            AutoSizeAxes = Axes.X;
            Height = 18;
            Masking = true;
            CornerRadius = 4;

            var content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Padding = new MarginPadding { Horizontal = 6 },
            };

            if (icon != null)
            {
                content.Add(new SpriteIcon
                {
                    Icon = icon.Value,
                    Size = new Vector2(9),
                    Colour = colour,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                });
            }

            content.Add(new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                Colour = colour,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            });

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colour.Opacity(0.14f) },
                content,
            };
        }

        /// <summary>The material/status tags of a track (shared by both surfaces).</summary>
        public static IEnumerable<TagPill> For(ForgeTrack track, OsuColour colours, OverlayColourProvider provider)
        {
            if (!File.Exists(track.Path))
            {
                yield return new TagPill(@"file missing", FontAwesome.Solid.ExclamationTriangle, colours.Red1,
                    $"The file was moved or deleted: {track.Path}. Remove the track, or put the file back.");
            }
            else if (track.AnalysisFailed)
            {
                yield return new TagPill(@"can't read", FontAwesome.Solid.ExclamationTriangle, colours.Orange1,
                    @"This file couldn't be decoded for its waveform. It may be damaged or in an unsupported format.");
            }

            if (track.LikelyEmpty)
            {
                yield return new TagPill(@"no meaningful audio?", FontAwesome.Solid.ExclamationTriangle, colours.Orange1,
                    @"This track seems to contain no meaningful audio — it was moved to the end of the list. Select it only if you are sure.");
            }

            if (track.DemuxPending && track.CanDemux)
            {
                yield return new TagPill(@"queued for demux", FontAwesome.Solid.CodeBranch, ForgeRoles.DemuxColour(colours),
                    @"Picked in the Demux card: it will be separated into stems when you run Demux.");
            }

            if (ForgeTrackSession.IsVideoAudio(track))
                yield return new TagPill(@"from video", FontAwesome.Solid.Film, provider.Content2, @"The audio extracted from the level's video.");

            if (track.IsClipped)
                yield return new TagPill(@"trimmed", FontAwesome.Solid.Cut, provider.Content2, @"Trimmed from its sides — only the part shown plays (drag the sides back out to restore).");

            if (track.IsMixdown)
                yield return new TagPill(@"mixdown", FontAwesome.Solid.Compress, provider.Content2, @"Mixed material (a render) — it cannot be demuxed.");
            else if (track.IsSeparated)
                yield return new TagPill(@"separated", FontAwesome.Solid.CodeBranch, provider.Content2, @"Already separated (or a stem) — it is not demuxed again.");
        }
    }

    /// <summary>
    /// A button in the editor's form vocabulary (same background, hover and
    /// commit flash as the stock Form* controls) with an icon and a label.
    /// </summary>
    internal partial class ForgeButton : OsuClickableContainer
    {
        private readonly IconUsage icon;
        private LocalisableString text;
        private readonly Color4? accent;

        private FormControlBackground background = null!;
        private FillFlowContainer content = null!;
        private OsuSpriteText? label;

        /// <summary>The button's caption (can change after load).</summary>
        public LocalisableString Text
        {
            get => text;
            set
            {
                text = value;
                if (label != null) label.Text = value;
            }
        }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public ForgeButton(IconUsage icon, LocalisableString text, Color4? accent = null)
        {
            this.icon = icon;
            this.text = text;
            this.accent = accent;

            RelativeSizeAxes = Axes.X;
            Height = 34;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 5;
            CornerExponent = FormControlBackground.CORNER_EXPONENT;

            Children = new Drawable[]
            {
                background = new FormControlBackground(),
                content = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(7, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Icon = icon,
                            Size = new Vector2(13),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = accent ?? colourProvider.Highlight1,
                        },
                        label = new OsuSpriteText
                        {
                            Text = text,
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Enabled.BindValueChanged(_ => updateStyle(), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateStyle();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateStyle();
            base.OnHoverLost(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (Enabled.Value)
                background.FlashOnCommit();

            return base.OnClick(e);
        }

        private void updateStyle()
        {
            content.FadeTo(Enabled.Value ? 1 : 0.4f, 150);

            if (!Enabled.Value)
                background.VisualStyle = VisualStyle.Disabled;
            else if (IsHovered)
                background.VisualStyle = VisualStyle.Hovered;
            else
                background.VisualStyle = VisualStyle.Normal;
        }
    }

    /// <summary>
    /// ✕ that asks once: the first click arms it ("remove?", red), a second
    /// click within a few seconds removes. Removing only takes the track out
    /// of the set — no file is deleted.
    /// </summary>
    internal partial class RemoveButton : OsuClickableContainer
    {
        private readonly Action onConfirm;

        private Box background = null!;
        private SpriteIcon cross = null!;
        private OsuSpriteText label = null!;
        private ScheduledDelegate? disarm;
        private bool armed;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public override LocalisableString TooltipText
        {
            get => armed ? @"click again to remove (the file itself is kept)" : @"remove from the set";
            set { }
        }

        public RemoveButton(Action onConfirm)
        {
            this.onConfirm = onConfirm;
            Size = new Vector2(26);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 13;

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3, Alpha = 0 },
                label = new OsuSpriteText
                {
                    Text = @"remove?",
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 10 },
                    Alpha = 0,
                },
                cross = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.Times,
                    Size = new Vector2(11),
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.Centre,
                    X = -13,
                    Colour = colourProvider.Content2,
                },
            };

            Action = click;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!armed)
            {
                background.FadeTo(1, 100);
                cross.FadeColour(colours.Red1, 100);
            }

            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!armed)
            {
                background.FadeTo(0, 200);
                cross.FadeColour(colourProvider.Content2, 200);
            }

            base.OnHoverLost(e);
        }

        private void click()
        {
            if (armed)
            {
                disarm?.Cancel();
                onConfirm();
                return;
            }

            armed = true;
            background.FadeColour(colours.Red3, 100).FadeTo(1, 100);
            cross.FadeColour(Color4.White, 100);
            label.FadeIn(150);
            this.ResizeWidthTo(84, 200, Easing.OutQuint);

            disarm?.Cancel();
            disarm = Scheduler.AddDelayed(() =>
            {
                armed = false;
                background.FadeColour(colourProvider.Background3, 200).FadeTo(IsHovered ? 1 : 0, 200);
                cross.FadeColour(IsHovered ? colours.Red1 : colourProvider.Content2, 200);
                label.FadeOut(100);
                this.ResizeWidthTo(26, 200, Easing.OutQuint);
            }, 3000);
        }
    }

    /// <summary>
    /// First-run guidance: a short chain of icon steps ("import → assign
    /// roles → optional actions") instead of a bare sentence.
    /// </summary>
    internal partial class HintChain : CompositeDrawable
    {
        private readonly (IconUsage icon, string text)[] steps;

        public HintChain(params (IconUsage icon, string text)[] steps)
        {
            this.steps = steps;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(8, 8),
                Padding = new MarginPadding(14),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                if (i > 0)
                {
                    flow.Add(new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.ChevronRight,
                        Size = new Vector2(9),
                        Colour = colourProvider.Foreground1,
                        Margin = new MarginPadding { Top = 9 },
                    });
                }

                flow.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(7, 0),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Size = new Vector2(26),
                            Masking = true,
                            CornerRadius = 13,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 },
                                new SpriteIcon
                                {
                                    Icon = steps[i].icon,
                                    Size = new Vector2(11),
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Colour = colourProvider.Highlight1,
                                },
                            },
                        },
                        new OsuSpriteText
                        {
                            Text = steps[i].text,
                            Font = OsuFont.Style.Caption1,
                            Colour = colourProvider.Content2,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                    },
                });
            }

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 1.5f,
                BorderColour = colourProvider.Background2,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5.Opacity(0.6f) },
                    flow,
                },
            };
        }
    }

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
