// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Utils;
using Kan.Forge.Media;
using Kan.Forge.Tracks;
using osuTK.Graphics;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's **Resources** section (stock osu! rows untouched) with the
    /// kan! import surface merged in (owner rules 2026-10-13):
    ///
    /// - **Resources is the ONLY place that reads files** — audio tracks
    ///   (multiple, via the navigator), and the stock rows above for video
    ///   and images. Nothing else anywhere asks for a path.
    /// - With "video provides the audio" on, the video's audio joins the
    ///   track list automatically (as an accompaniment).
    /// - Everything downstream (roles, track editing, demux/transcribe/render
    ///   actions) happens on the **tracks** tab and only *selects* from the
    ///   resources imported or created here.
    /// </summary>
    public partial class KanMediaSection : ResourcesSection
    {
        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private FormCheckBox videoProvidesAudio = null!;
        private OsuTextFlowContainer videoEffect = null!;
        private FormFileSelector trackSelector = null!;
        private OsuSpriteText setSummary = null!;
        private FillFlowContainer explorer = null!;

        private readonly ForgeTrackSet trackSet = ForgeTrackSession.Tracks;
        private readonly HashSet<string> extractedVideos = new();
        private readonly HashSet<ForgeTrack> seenTracks = new();
        private SetSignature drawnSignature;
        private bool everDrawn;
        private bool extractingVideo;
        private string? drawnVideoEffect;

        private static bool devToolsEnabled
            => DebugUtils.IsDebugBuild
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(@"KAN_DEV_TOOLS"));

        [Resolved]
        private osu.Framework.Platform.Storage storage { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void loadKan()
        {
            // The level's saved set (no-op if the track tab opened it first).
            foreach (var track in ForgeTrackSession.Open(storage.GetFullPath(@"kan-forge"), working.Value.BeatmapSetInfo.ID.ToString("N")))
                analyzeLater(track);

            // The stock rows (background, video, audio, samples) are already
            // in the section's flow at this point.
            var stockRows = Children.ToList();

            videoProvidesAudio = new FormCheckBox
            {
                Caption = @"Video provides the audio",
                HintText = @"When the clip's audio lives inside the video, it joins the set as a companion track. Turn off to use the video for visuals only.",
                Current = new BindableBool(true),
            };
            videoEffect = new OsuTextFlowContainer(t =>
            {
                t.Font = OsuFont.Style.Caption2;
                t.Colour = colourProvider.Foreground1;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Horizontal = 9 },
            };
            trackSelector = new FormFileSelector(SupportedExtensions.AUDIO_EXTENSIONS)
            {
                Caption = @"Audio tracks",
                HintText = @"Every file you pick joins the set below: the reference performance, companion tracks, your own stems. The same file is never added twice.",
                PlaceholderText = @"Click to add a track (repeat for more), or drop files here",
            };
            setSummary = new OsuSpriteText
            {
                Font = OsuFont.Style.Caption2,
                Colour = colourProvider.Foreground1,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
            };
            explorer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 5),
            };

            var videoBlock = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 4),
                Children = new Drawable[] { videoProvidesAudio, videoEffect },
            };
            var audioLink = new AudioTrackLink(trackSet, working);
            var setBlock = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
                Margin = new MarginPadding { Top = 4 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 9 },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = @"The set",
                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                Colour = colourProvider.Content2,
                            },
                            setSummary,
                        },
                    },
                    explorer,
                },
            };

            var added = new List<Drawable> { videoBlock, audioLink, trackSelector, setBlock };

            if (devToolsEnabled)
            {
                added.Add(new FormTextBox
                {
                    Caption = @"Source audio URL (developer tooling)",
                    PlaceholderText = @"https://… — fetched via yt-dlp; not part of the official flow",
                });
            }

            AddRange(added);
            placeAfterVideoRow(stockRows, added);
            hideStockAudioSelector();

            // The navigator applies immediately (like the stock rows) and
            // clears itself so more tracks can be picked right away.
            trackSelector.Current.BindValueChanged(change =>
            {
                if (change.NewValue == null) return;
                addTrack(change.NewValue);
                trackSelector.Current.Value = null;
            });

            syncExplorer();
        }

        /// <summary>
        /// Weave the kan! rows into the stock flow instead of appending them
        /// below it: background, video, *video provides the audio*, *Audio
        /// Track (connected to the music role)*, *audio tracks*, *the set*,
        /// then the stock sample rows. Layout order only; if the stock layout
        /// is not what we expect, the rows simply stay appended.
        /// </summary>
        private void placeAfterVideoRow(List<Drawable> stockRows, List<Drawable> added)
        {
            try
            {
                if (Content is not FillFlowContainer flow) return;

                var video = typeof(ResourcesSection).GetField("videoChooser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(this) as Drawable;

                int anchor = video != null ? stockRows.IndexOf(video) : -1;
                if (anchor < 0) return;

                for (int i = 0; i < stockRows.Count; i++)
                    flow.SetLayoutPosition(stockRows[i], i * 100);

                for (int i = 0; i < added.Count; i++)
                    flow.SetLayoutPosition(added[i], anchor * 100 + 1 + i);
            }
            catch
            {
                // Never break the editor over layout.
            }
        }

        /// <summary>
        /// Owner rule 2026-10-13: Resources has ONE audio import surface (the
        /// navigator above). The upstream "Audio Track" selector is hidden —
        /// the 'music' role connects the chosen track to the beatmap audio
        /// instead. Implemented via reflection on an upstream private field:
        /// if a future upstream rename hides the change, the row merely
        /// reappears (harmless), and the behaviour is tracked in the
        /// fork-divergence log.
        /// </summary>
        private void hideStockAudioSelector()
        {
            try
            {
                var field = typeof(ResourcesSection).GetField("audioTrackChooser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field?.GetValue(this) is Drawable chooser)
                    chooser.Expire();
            }
            catch
            {
                // Never break the editor over cosmetics.
            }
        }

        // ------------------------------------------------------------------
        // Video -> track list (merge)
        // ------------------------------------------------------------------

        protected override void Update()
        {
            base.Update();
            maybeExtractVideoAudio();

            if (--explorerCountdown <= 0)
            {
                explorerCountdown = 10;
                syncExplorer();
                updateVideoEffect();
                ForgeTrackSession.AutoSave(SetSignature.Of(trackSet).Full + (ForgeTrackSession.Draft?.Notes.Count ?? -1), Time.Current);
            }
        }

        private int explorerCountdown;

        /// <summary>
        /// When the beatmap gains a video and "video provides the audio" is
        /// on, its audio joins the track list (as an accompaniment — the user
        /// can re-role it). Runs at most every 30 frames.
        /// </summary>
        private int videoCheckCountdown;

        private string? currentVideoFile()
            => working.Value.BeatmapSetInfo.Files
                      .Select(f => f.Filename)
                      .FirstOrDefault(MediaAudioExtractor.IsVideo);

        private void maybeExtractVideoAudio()
        {
            if (--videoCheckCountdown > 0) return;
            videoCheckCountdown = 30;

            if (!videoProvidesAudio.Current.Value) return;

            string? videoFile = currentVideoFile();
            if (videoFile == null || extractedVideos.Contains(videoFile)) return;

            extractedVideos.Add(videoFile);
            extractingVideo = true;
            updateVideoEffect();

            Task.Run(() => extractVideoAudio(videoFile))
                .ContinueWith(task => Scheduler.Add(() =>
                {
                    extractingVideo = false;

                    if (task.IsFaulted)
                    {
                        ForgeNotify.Error(notifications, $"Video audio extraction failed: {task.Exception?.GetBaseException().Message}");
                        updateVideoEffect();
                        return;
                    }

                    var track = trackSet.Import(Path.GetFileNameWithoutExtension(videoFile) + @" (video audio)", task.Result);
                    if (track == null) return;

                    track.IsCompanion = true;
                    ForgeTrackSession.MarkVideoAudio(track);
                    analyzeLater(track);
                    syncExplorer();
                    updateVideoEffect();
                    ForgeNotify.Info(notifications, $"The video's audio joined the set as a companion track ({trackSet.Tracks.Count} tracks).", FontAwesome.Solid.Film);
                }));
        }

        /// <summary>Make the toggle's effect visible: what happened (or will happen) to the video's audio.</summary>
        private void updateVideoEffect()
        {
            string? videoFile = currentVideoFile();
            var fromVideo = trackSet.Tracks.ToArray().FirstOrDefault(t => t != null && ForgeTrackSession.IsVideoAudio(t));

            string text;

            if (videoFile == null)
                text = videoProvidesAudio.Current.Value
                    ? @"↳ no video yet — once you add one above, its audio joins the set."
                    : @"↳ visuals only — a video's audio will stay out of the set.";
            else if (!videoProvidesAudio.Current.Value)
                text = fromVideo != null
                    ? $"↳ visuals only from now on — '{fromVideo.Name}' is already in the set (remove it there if unwanted)."
                    : @"↳ visuals only — the video's audio stays out of the set.";
            else if (extractingVideo)
                text = @"↳ extracting the video's audio…";
            else if (fromVideo != null)
                text = $"↳ the video's audio is in the set as '{fromVideo.Name}'" + (fromVideo.IsCompanion ? @" (companion)." : @".");
            else
                text = @"↳ the video's audio joins the set.";

            if (drawnVideoEffect == text) return;

            drawnVideoEffect = text;
            videoEffect.Text = text;
        }

        private string extractVideoAudio(string videoFile)
        {
            string? src = materialise(videoFile);
            if (src == null)
                throw new InvalidOperationException("video audio extraction failed: cannot read the video from the beatmap set");

            string outWav = ForgeTrackSession.WorkPath(Path.GetFileNameWithoutExtension(videoFile) + "-audio.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            return MediaAudioExtractor.TryExtractToWav(src, outWav, out string error)
                ? outWav
                : throw new InvalidOperationException($"video audio extraction failed: {error}");
        }

        /// <summary>Copy a beatmap-set file to a temp path the bridge can read.</summary>
        private string? materialise(string fileName)
        {
            var beatmap = working.Value;
            string tmp = ForgeTrackSession.WorkPath(fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);

            // Files inside a level are stored by hash: resolve the storage path.
            var stored = beatmap.BeatmapSetInfo.GetFile(fileName);
            if (stored == null) return null;

            using (var src = beatmap.GetStream(stored.File.GetStoragePath()))
            {
                if (src == null) return null;

                using var dst = File.Create(tmp);
                src.CopyTo(dst);
            }

            return tmp;
        }

        // ------------------------------------------------------------------
        // Import (the single file-reading surface)
        // ------------------------------------------------------------------

        private void addTrack(FileInfo file)
        {
            if (!file.Exists)
            {
                ForgeNotify.Error(notifications, $"Import: '{file.Name}' does not exist.");
                return;
            }

            var track = trackSet.Import(Path.GetFileNameWithoutExtension(file.Name), file.FullName);
            if (track == null)
            {
                ForgeNotify.Info(notifications, $"'{file.Name}' is already in the set — a track is never added twice.", FontAwesome.Solid.Clone);
                explorer.OfType<ExplorerRow>().FirstOrDefault(r => string.Equals(r.Track.Path, file.FullName, StringComparison.OrdinalIgnoreCase))?.Pulse();
                return;
            }

            track.IsMixdown = ForgeTrack.LooksLikeMixdown(file.Name);
            analyzeLater(track);
            syncExplorer();
        }

        private void analyzeLater(ForgeTrack track)
            => Task.Run(() => TrackAnalysis.Analyze(track));

        /// <summary>
        /// The whole set as a compact explorer (owner 2026-10-13): every
        /// track with its waveform as the row background and its roles as
        /// chips — selected tracks framed in their role's colour, never just
        /// a count. Rebuilt only when the set actually changes.
        /// </summary>
        private void syncExplorer()
        {
            // Durations arrive after the first draw: redraw once they're known.
            var signature = SetSignature.Of(trackSet, t => TrackDurations.Get(t.Path) != null ? @"d" : string.Empty);
            if (everDrawn && signature == drawnSignature) return;

            drawnSignature = signature;
            everDrawn = true;

            explorer.Clear();

            var tracks = trackSet.Tracks.ToArray();
            int selected = tracks.Count(t => t != null && t.HasAnyRole);

            setSummary.Text = tracks.Length == 0
                ? string.Empty
                : $"{ForgeFormat.Plural(tracks.Length, "track", "tracks")} · {selected} with a role";

            if (tracks.Length == 0)
            {
                explorer.Add(new HintChain(
                    (FontAwesome.Solid.FileImport, @"add audio above"),
                    (FontAwesome.Solid.Music, @"pick roles in the track tab")));
                return;
            }

            bool firstDraw = seenTracks.Count == 0;

            foreach (var track in tracks)
            {
                if (track == null) continue;

                var row = new ExplorerRow(track, removeFromSet);
                explorer.Add(row);

                if (seenTracks.Add(track) && !firstDraw)
                    row.Pulse();
            }
        }

        private void removeFromSet(ForgeTrack track)
        {
            trackSet.Remove(track);
            syncExplorer();
        }

        /// <summary>Compact row: waveform background + name + role chips + tags + remove.</summary>
        private partial class ExplorerRow : CompositeDrawable
        {
            public readonly ForgeTrack Track;

            private readonly Action<ForgeTrack> onRemove;

            private Box flashLayer = null!;
            private TruncatingSpriteText nameText = null!;
            private FillFlowContainer details = null!;
            private RemoveButton remove = null!;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public ExplorerRow(ForgeTrack track, Action<ForgeTrack> onRemove)
            {
                Track = track;
                this.onRemove = onRemove;

                RelativeSizeAxes = Axes.X;
                Height = 48;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var primary = ForgeRoles.Primary(Track);
                Color4 highlight = primary != null ? ForgeRoles.Colour(colours, primary.Value) : colourProvider.Background4;
                Color4 background = colourProvider.Background4.Darken(0.1f);

                details = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new osuTK.Vector2(4, 0),
                };

                foreach (var role in ForgeRoles.ALL)
                {
                    if (!ForgeRoles.Has(Track, role)) continue;

                    var chip = new RoleChip(role, compact: true);
                    chip.SetState(true);
                    details.Add(chip);
                }

                details.AddRange(TagPill.For(Track, colours, colourProvider));

                if (TrackDurations.Get(Track.Path) is double seconds)
                {
                    details.Add(new OsuSpriteText
                    {
                        Text = ForgeFormat.Time(seconds),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                        Colour = colourProvider.Foreground1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    });
                }

                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 5,
                    CornerExponent = FormControlBackground.CORNER_EXPONENT,
                    BorderThickness = primary != null ? 2 : 0,
                    BorderColour = highlight,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = background },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 6 },
                            Alpha = Track.Muted ? 0.25f : 0.55f,
                            Child = new WaveformPreview(Track)
                            {
                                RelativeSizeAxes = Axes.Both,
                                AccentColour = WaveformPreview.ColourFor(Track, colours, colourProvider),
                            },
                        },
                        // Legibility: the text side fades the waveform out.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.7f,
                            Colour = ColourInfo.GradientHorizontal(background.Opacity(0.9f), background.Opacity(0)),
                        },
                        new Box { RelativeSizeAxes = Axes.Y, Width = 4, Colour = highlight, Alpha = primary != null ? 1 : 0 },
                        flashLayer = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Highlight1, Alpha = 0 },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Vertical,
                            Spacing = new osuTK.Vector2(0, 3),
                            Padding = new MarginPadding { Left = 12 },
                            Children = new Drawable[]
                            {
                                nameText = new TruncatingSpriteText
                                {
                                    Text = Track.Name,
                                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                    Alpha = Track.LikelyEmpty ? 0.6f : 1,
                                },
                                details,
                            },
                        },
                        remove = new RemoveButton(() => onRemove(Track))
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Margin = new MarginPadding { Right = 8 },
                        },
                    },
                };
            }

            protected override void Update()
            {
                base.Update();
                nameText.MaxWidth = Math.Max(60, DrawWidth - 12 - remove.DrawWidth - 24);
            }

            /// <summary>Draw the eye to this row (just added, or a refused duplicate of it).</summary>
            public void Pulse()
            {
                if (LoadState < LoadState.Ready)
                {
                    Schedule(Pulse);
                    return;
                }

                flashLayer.FadeTo(0.35f).FadeOut(1200, Easing.OutQuint);
            }
        }

        /// <summary>
        /// The editor's "Audio Track", shown where the stock row was: not a
        /// second import — it is the level audio, the mix of the companion
        /// tracks (owner naming 2026-10-14). Shows which tracks it mixes, the
        /// file it plays as and whether it is up to date; the picker toggles
        /// companion tracks, and closing it renders the level audio if the
        /// mix changed.
        /// </summary>
        private partial class AudioTrackLink : CompositeDrawable, IHasPopover
        {
            private readonly ForgeTrackSet set;
            private readonly IBindable<WorkingBeatmap> working;
            private readonly Bindable<Visibility> popoverState = new Bindable<Visibility>();

            [Resolved]
            private BeatmapManager beatmaps { get; set; } = null!;

            [Resolved]
            private MusicController music { get; set; } = null!;

            [Resolved]
            private osu.Game.Screens.Edit.Editor? editor { get; set; }

            [Resolved]
            private INotificationOverlay? notifications { get; set; }

            private FormControlBackground background = null!;
            private OsuTextFlowContainer text = null!;
            private SpriteIcon linkIcon = null!;
            private string? drawnKey;
            private int countdown;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public AudioTrackLink(ForgeTrackSet set, IBindable<WorkingBeatmap> working)
            {
                this.set = set;
                this.working = working;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    background = new FormControlBackground(),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new osuTK.Vector2(0, 4),
                        Padding = new MarginPadding { Left = 9, Right = 32, Vertical = 9 },
                        Children = new Drawable[]
                        {
                            new FormFieldCaption
                            {
                                Caption = @"Audio Track (level audio)",
                                TooltipText = @"What plays under the player: the mix of the companion tracks. Click to choose them — new audio is imported in 'Audio tracks' below.",
                            },
                            text = new OsuTextFlowContainer(t => t.Font = OsuFont.Default.With(size: 16))
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                        },
                    },
                    linkIcon = new SpriteIcon
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Margin = new MarginPadding(9),
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new osuTK.Vector2(13),
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                popoverState.BindValueChanged(v =>
                {
                    updateStyle();

                    // Closing the picker brings the level audio up to date.
                    if (v.NewValue == Visibility.Hidden && set.Companions.Any() && !BeatmapAudio.IsUpToDate(set))
                        render();
                });
            }

            private void render()
            {
                if (BeatmapAudio.Rendering) return;

                renderTask = BeatmapAudio.RenderAsync(set, working.Value, beatmaps, music, editor, Scheduler);
                renderTask.ContinueWith(t => Schedule(() =>
                {
                    if (t.IsFaulted)
                        ForgeNotify.Error(notifications, $"Rendering the level audio failed: {t.Exception?.GetBaseException().Message}");
                    else
                        ForgeNotify.Info(notifications, $"The level audio is up to date: \"{t.Result}\".", FontAwesome.Solid.LayerGroup);

                    countdown = 0;
                }));
                countdown = 0;
            }

            private System.Threading.Tasks.Task<string>? renderTask;

            protected override bool OnHover(HoverEvent e)
            {
                updateStyle();
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);
                updateStyle();
            }

            protected override bool OnClick(ClickEvent e)
            {
                this.ShowPopover();
                return true;
            }

            private void updateStyle()
            {
                if (popoverState.Value == Visibility.Visible)
                    background.VisualStyle = VisualStyle.Focused;
                else
                    background.VisualStyle = IsHovered ? VisualStyle.Hovered : VisualStyle.Normal;
            }

            public Popover GetPopover()
            {
                var popover = new CompanionPickerPopover(set, toggle);
                popoverState.UnbindBindings();
                popoverState.BindTo(popover.State);
                return popover;
            }

            private void toggle(ForgeTrack track)
            {
                ForgeTrackSession.History.Record();
                track.IsCompanion = !track.IsCompanion;
                countdown = 0;
            }

            protected override void Update()
            {
                base.Update();

                if (--countdown > 0) return;
                countdown = 15;

                ForgeTrack[] companions;

                try
                {
                    companions = set.Companions.ToArray();
                }
                catch (InvalidOperationException)
                {
                    return; // the demux worker is adding stems; next poll
                }

                string audioFile = working.Value.Metadata.AudioFile ?? string.Empty;
                bool upToDate = BeatmapAudio.IsUpToDate(set);
                bool rendering = BeatmapAudio.Rendering;
                string key = $"{string.Join('|', companions.Select(c => c.Name))}#{audioFile}#{upToDate}#{rendering}";
                if (key == drawnKey) return;

                bool changed = drawnKey != null;
                drawnKey = key;

                text.Clear();
                var colour = ForgeRoles.Colour(colours, ForgeRole.Companion);

                if (companions.Length > 0)
                {
                    text.AddIcon(FontAwesome.Solid.LayerGroup, i => i.Colour = colour);
                    text.AddText(@"  ");
                    text.AddText(companions.Length == 1 ? companions[0].Name : $"mix of {companions.Length} companion tracks",
                        t => t.Font = OsuFont.Default.With(size: 16, weight: FontWeight.SemiBold));

                    string state = rendering ? @"rendering…"
                        : upToDate && !string.IsNullOrEmpty(audioFile) ? $"plays as {audioFile}"
                        : @"changed — updates when you close this or leave the track tab";
                    text.AddText($"   {state}", t => t.Colour = colourProvider.Foreground1);

                    if (companions.Length > 1)
                    {
                        text.NewParagraph();
                        text.AddText(string.Join(@" · ", companions.Select(c => c.Name)), t =>
                        {
                            t.Font = OsuFont.Style.Caption2;
                            t.Colour = colourProvider.Content2;
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(audioFile))
                {
                    text.AddText(audioFile);
                    text.AddText(@"   no companion tracks yet — click to choose them", t => t.Colour = colourProvider.Foreground1);
                }
                else
                    text.AddText(@"No level audio yet — click to choose the companion tracks", t => t.Colour = colourProvider.Foreground1);

                linkIcon.Colour = companions.Length > 0 ? colour : colourProvider.Light1;

                if (changed)
                    background.FlashOnCommit();
            }
        }
        /// <summary>The level audio picker: toggle companion tracks (selection only — nothing is imported here).</summary>
        private partial class CompanionPickerPopover : OsuPopover
        {
            private readonly ForgeTrackSet set;
            private readonly Action<ForgeTrack> onChoose;

            public CompanionPickerPopover(ForgeTrackSet set, Action<ForgeTrack> onChoose)
                : base(false)
            {
                this.set = set;
                this.onChoose = onChoose;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                var tracks = set.Tracks.ToArray().Where(t => t != null).ToArray();

                var list = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 4),
                };

                if (tracks.Length == 0)
                {
                    list.Add(new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption1;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(8),
                        Text = @"The set is empty — add audio with 'Audio tracks' below, then choose the companion tracks here.",
                    });
                }

                foreach (var track in tracks)
                    list.Add(new PickerRow(track, () => onChoose(track)));

                Child = new OsuScrollContainer
                {
                    Width = 380,
                    Height = Math.Clamp(tracks.Length * 48f + 8, 60, 320),
                    Child = list,
                };
            }
        }

        private partial class PickerRow : OsuClickableContainer
        {
            private readonly ForgeTrack track;
            private Box hover = null!;
            private SpriteIcon check = null!;
            private Color4 on;
            private Color4 off;

            public override LocalisableString TooltipText
            {
                get => track.IsCompanion ? @"companion — click to remove it from the level audio" : @"click to make it a companion (part of the level audio)";
                set { }
            }

            protected override void Update()
            {
                base.Update();
                check.Icon = track.IsCompanion ? FontAwesome.Solid.CheckSquare : FontAwesome.Regular.Square;
                check.Colour = track.IsCompanion ? on : off;
            }

            public PickerRow(ForgeTrack track, Action onChoose)
            {
                this.track = track;
                Action = onChoose;
                RelativeSizeAxes = Axes.X;
                Height = 44;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider, OsuColour colours)
            {
                Masking = true;
                CornerRadius = 5;
                on = ForgeRoles.Colour(colours, ForgeRole.Companion);
                off = colourProvider.Content2;

                var details = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new osuTK.Vector2(4, 0),
                };

                foreach (var role in ForgeRoles.ALL)
                {
                    if (!ForgeRoles.Has(track, role)) continue;

                    var chip = new RoleChip(role, compact: true);
                    chip.SetState(true);
                    details.Add(chip);
                }

                details.AddRange(TagPill.For(track, colours, colourProvider));

                if (TrackDurations.Get(track.Path) is double seconds)
                {
                    details.Add(new OsuSpriteText
                    {
                        Text = ForgeFormat.Time(seconds),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                        Colour = colourProvider.Foreground1,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    });
                }

                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 140, Right = 6 },
                        Alpha = 0.4f,
                        Child = new WaveformPreview(track)
                        {
                            RelativeSizeAxes = Axes.Both,
                            AccentColour = WaveformPreview.ColourFor(track, colours, colourProvider),
                        },
                    },
                    hover = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Highlight1, Alpha = 0 },
                    check = new SpriteIcon
                    {
                        Size = new osuTK.Vector2(13),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = 16,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Vertical,
                        Spacing = new osuTK.Vector2(0, 3),
                        Padding = new MarginPadding { Left = 32 },
                        Children = new Drawable[]
                        {
                            new TruncatingSpriteText
                            {
                                Text = track.Name,
                                MaxWidth = 320,
                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                                Alpha = track.LikelyEmpty ? 0.6f : 1,
                            },
                            details,
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                hover.FadeTo(0.15f, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeOut(200);
                base.OnHoverLost(e);
            }
        }
    }
}
