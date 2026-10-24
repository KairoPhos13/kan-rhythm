// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Setup;
using Kan.Forge.Media;
using Kan.Forge.Native;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's **track** tab (owner 2026-10-13: second, after setup —
    /// the first tool used when creating a level). It shows the unified
    /// resource list as <see cref="KanTrackRow"/>s — waveform, role chips,
    /// mute, and the non-destructive range editor on demand — plus the
    /// optional actions as <see cref="KanActionCard"/>s with their own
    /// progress. Everything here is **selection and arrangement**:
    /// importing happens only in Resources (<see cref="KanMediaSection"/>).
    /// Same canvas as the setup tab so the editor reads as one program.
    /// </summary>
    public partial class KanTracksScreen : EditorScreen, IKeyBindingHandler<PlatformAction>
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        private static ForgeSetHistory history => ForgeTrackSession.History;

        private ForgeButton undoButton = null!;
        private ForgeButton redoButton = null!;
        private ForgeButton clearSoloButton = null!;
        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private readonly ForgeTrackSet trackSet = ForgeTrackSession.Tracks;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private osu.Game.Screens.Edit.Editor? editor { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Cached]
        private readonly SetMonitor monitor = new SetMonitor(ForgeTrackSession.Tracks);

        private Container column = null!;
        private FillFlowContainer trackFlow = null!;
        private FillFlowContainer cardFlow = null!;
        private KanActionCard demuxCard = null!;
        private KanActionCard transcribeCard = null!;
        private KanActionCard renderCard = null!;
        private TrackPickList demuxPicks = null!;
        private FillFlowContainer arrangeView = null!;
        private MixerView mixView = null!;
        private ViewTab arrangeTab = null!;
        private ViewTab mixTab = null!;
        private TrackPickList transcribePicks = null!;

        /// <summary>Per-track UI state (expanded, range, name) kept across rebuilds.</summary>
        private readonly Dictionary<ForgeTrack, TrackRowState> rowStates = new();

        private readonly HashSet<ForgeTrack> seenTracks = new();
        private SetSignature drawnSignature;
        private bool everDrawn;

        /// <summary>The track a job is working on right now (row spinner), with its label.</summary>
        private ForgeTrack? workingTrack;

        private string? workingLabel;

        public KanTracksScreen()
            : base(EditorScreenMode.Tracks)
        {
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            openLevelSet();

            demuxCard = new KanActionCard(FontAwesome.Solid.CodeBranch, @"Demux", @"separate", ForgeRoles.DemuxColour(colours), startDemux);
            transcribeCard = new KanActionCard(FontAwesome.Solid.Microphone, @"Transcribe", @"transcribe", ForgeRoles.Colour(colours, ForgeRole.Reference), startTranscription);
            renderCard = new KanActionCard(FontAwesome.Solid.LayerGroup, @"Level audio file", string.Empty, ForgeRoles.Colour(colours, ForgeRole.Companion), () => renderLevelAudio(false), showButton: false);

            demuxPicks = new TrackPickList(false, ForgeRoles.DemuxColour(colours),
                t => t.DemuxPending,
                t => t.CanDemux || t.DemuxPending ? null : t.IsMixdown ? @"mixed material" : @"already separated",
                t =>
                {
                    history.Record();
                    t.DemuxPending = !t.DemuxPending;
                    onRoleChanged();
                });
            transcribePicks = new TrackPickList(true, ForgeRoles.Colour(colours, ForgeRole.Reference),
                t => t.IsReference,
                _ => null,
                t =>
                {
                    history.Record();
                    if (t.IsReference)
                        t.IsReference = false;
                    else
                        trackSet.SetReference(t);
                    onRoleChanged();
                });
            demuxCard.SetPicker(demuxPicks);
            transcribeCard.SetPicker(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
                Children = new Drawable[]
                {
                    transcribePicks,
                    new InstrumentDropdown(),
                },
            });

            trackFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 8),
            };

            cardFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new osuTK.Vector2(10),
                Children = new Drawable[] { demuxCard, transcribeCard },
            };

            renderCard.RelativeSizeAxes = Axes.X;
            mixView = new MixerView(trackSet, renderCard, onRoleChanged) { Alpha = 0 };

            // Same canvas as the setup tab (background, padding, centred
            // column) so switching tabs feels like one program.
            Add(monitor);
            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider.Background3,
            });

            Add(new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(15),
                Child = column = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new osuTK.Vector2(0, 10),
                        Padding = new MarginPadding { Bottom = 40 },
                        Children = new Drawable[]
                        {
                            createViewSwitch(),
                            arrangeView = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new osuTK.Vector2(0, 10),
                                Children = new Drawable[]
                                {
                                    sectionHeader(@"Tracks", @"press play below to hear the set · drag a track to move it, its sides to trim it · click to jump there · chips: companion (plays under the player) and reference (the full performance)", colourProvider),
                                    createToolbar(),
                                    trackFlow,
                                    sectionHeader(@"Actions", @"optional and independent — run any of them, in any order, or none", colourProvider).With(d => d.Margin = new MarginPadding { Top = 16 }),
                                    cardFlow,
                                },
                            },
                            mixView,
                        },
                    },
                },
            });

            syncRows();
        }

        private static Drawable sectionHeader(string title, string subtitle, OverlayColourProvider colourProvider) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new osuTK.Vector2(0, 2),
            Children = new Drawable[]
            {
                new SectionHeader(title) { Margin = new MarginPadding { Left = 9 } },
                new OsuTextFlowContainer(t =>
                {
                    t.Font = OsuFont.Style.Caption2;
                    t.Colour = colourProvider.Foreground1;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = 9 },
                    Text = subtitle,
                },
            },
        };

        private int pollCountdown;

        protected override void Update()
        {
            base.Update();

            // Same width rule as the setup tab's two-column layout.
            float available = DrawWidth - 30;
            column.RelativeSizeAxes = available > SetupScreen.MAX_WIDTH ? Axes.None : Axes.X;
            column.Width = available > SetupScreen.MAX_WIDTH ? SetupScreen.MAX_WIDTH : 1;

            // Cards side by side when they fit, stacked otherwise.
            float cardWidth = cardFlow.DrawWidth >= 2 * 250 + 10 ? (cardFlow.DrawWidth - 10) / 2 : cardFlow.DrawWidth;
            foreach (var card in cardFlow)
                card.Width = cardWidth;

            undoButton.Enabled.Value = history.CanUndo;
            redoButton.Enabled.Value = history.CanRedo;
            clearSoloButton.Alpha = monitor.AnySoloed ? 1 : 0;

            // Imports, analyses and stems land asynchronously: follow them.
            if (--pollCountdown <= 0)
            {
                pollCountdown = 10;
                syncRows();
                updateCardUsage(); // the level audio status changes without a set change (rendering)
                ForgeTrackSession.AutoSave(SetSignature.Of(trackSet).Full + (ForgeTrackSession.Draft?.Notes.Count ?? -1), Time.Current);
            }
        }

        // ------------------------------------------------------------------
        // Track list
        // ------------------------------------------------------------------

        private void removeTrack(ForgeTrack track)
        {
            history.Record();
            trackSet.Remove(track);
            syncRows();
        }

        private void onRoleChanged()
        {
            // Deferred: role toggles fire from inside a row's own controls.
            Scheduler.Add(syncRows);
        }

        private string workingKey(ForgeTrack t)
            => (ReferenceEquals(t, workingTrack) ? @"W" + workingLabel : string.Empty)
               + (monitor.IsSoloed(t) ? @"S" : string.Empty)
               + (monitor.AnySoloed ? @"s" : string.Empty);

        /// <summary>
        /// Bring the rows in line with the shared set: rebuild when tracks
        /// were added/removed/reordered (UI state per track survives), refresh
        /// in place when only roles/flags/analysis changed.
        /// </summary>
        private void syncRows()
        {
            var signature = SetSignature.Of(trackSet, workingKey);

            if (everDrawn && signature == drawnSignature)
                return;

            bool rebuild = !everDrawn || signature.Order != drawnSignature.Order;
            drawnSignature = signature;
            everDrawn = true;

            if (rebuild)
                rebuildTrackRows();
            else
            {
                foreach (var row in trackFlow.OfType<KanTrackRow>())
                    row.Refresh(ReferenceEquals(row.Track, workingTrack), workingLabel);
            }

            updateCardUsage();
        }

        private void rebuildTrackRows()
        {
            trackFlow.Clear();

            var tracks = trackSet.Tracks.ToArray();

            if (tracks.Length == 0)
            {
                trackFlow.Add(new HintChain(
                    (FontAwesome.Solid.FileImport, @"import audio in Resources (setup)"),
                    (FontAwesome.Solid.Music, @"give tracks a role with the chips"),
                    (FontAwesome.Solid.Magic, @"optionally demux, transcribe or render")));
                return;
            }

            bool firstDraw = seenTracks.Count == 0;

            foreach (var track in tracks)
            {
                if (!rowStates.TryGetValue(track, out var state))
                    rowStates[track] = state = new TrackRowState();

                var row = new KanTrackRow(trackSet, track, state, removeTrack, onRoleChanged, saveEdits);
                row.Refresh(ReferenceEquals(track, workingTrack), workingLabel);
                trackFlow.Add(row);

                if (seenTracks.Add(track) && !firstDraw)
                    row.FlashNew();
            }

            // Forget UI state of tracks that left the set.
            foreach (var gone in rowStates.Keys.Where(t => !tracks.Contains(t)).ToList())
                rowStates.Remove(gone);
        }

        private KanTrackRow? rowFor(ForgeTrack track) => trackFlow.OfType<KanTrackRow>().FirstOrDefault(r => ReferenceEquals(r.Track, track));

        private void setWorking(ForgeTrack? track, string? label)
        {
            workingTrack = track;
            workingLabel = label;
            syncRows();
        }

        /// <summary>What each action will use, read live from the chips.</summary>
        private void updateCardUsage()
        {
            try
            {
                updateCardUsageUnsafe();
            }
            catch (InvalidOperationException)
            {
                // The demux worker added a stem mid-enumeration: next poll.
                drawnSignature = default;
            }
        }

        private void updateCardUsageUnsafe()
        {
            var all = trackSet.Tracks.ToArray().Where(t => t != null).ToList();
            demuxPicks.SetTracks(all);
            transcribePicks.SetTracks(all);

            int queued = trackSet.DemuxQueue.Count();
            demuxCard.SetUsage(queued > 0
                ? $"separates {ForgeFormat.Plural(queued, "track", "tracks")} into stems that join the set:"
                : @"pick the tracks to separate into stems:", queued > 0);

            var reference = trackSet.Reference;
            transcribeCard.SetUsage(reference != null
                ? $"transcribes '{reference.Name}' (the reference):"
                : @"pick the track to transcribe (it becomes the reference):", reference != null);

            int mixed = trackSet.Companions.Count();
            string window = trackSet.HasAudioWindow
                ? $" Kept: {ForgeFormat.Time(trackSet.AudioStartMs / 1000)} – {(trackSet.AudioEndMs > 0 ? ForgeFormat.Time(trackSet.AudioEndMs / 1000) : "end")}."
                : string.Empty;
            string state = mixed == 0 ? string.Empty
                : BeatmapAudio.Rendering ? " Updating…"
                : BeatmapAudio.IsUpToDate(trackSet) ? " Up to date." : " Changed — it updates by itself when you leave this tab or save.";
            renderCard.SetUsage(mixed > 0
                ? $"What gameplay plays: the {ForgeFormat.Plural(mixed, "companion track", "companion tracks")}, mixed.{window}{state}"
                : @"What gameplay plays: the mix of the companion tracks — none marked yet.", mixed > 0);
        }

        /// <summary>Refresh the list (called by the import surface).</summary>
        public void RefreshTracks() => Scheduler.Add(syncRows);

        protected override void PopOut()
        {
            base.PopOut();

            // osu!framework also calls this once while the screen is still
            // loading, on the loading thread: nothing to stop or render yet.
            if (!IsLoaded) return;

            monitor.Active.Value = false;

            // The level audio follows the companion mix: leaving the tab
            // brings it up to date for the other tabs.
            updateLevelAudioIfChanged();
        }

        protected override void PopIn()
        {
            base.PopIn();
            monitor.Active.Value = true;
            // Imports land in the shared session while this screen is away;
            // refresh whenever the user comes back to the tab.
            syncRows();
        }

        /// <summary>Analyse a track off-thread (waveform + meaningful-audio marking).</summary>
        private void analyzeLater(ForgeTrack track)
            => Task.Run(() => TrackAnalysis.Analyze(track))
                   .ContinueWith(_ => Scheduler.Add(syncRows));

        // ------------------------------------------------------------------
        // Track editor (non-destructive: edits create NEW named tracks)
        // ------------------------------------------------------------------

        /// <summary>
        /// "Save as new track": render the source's pending edits and filters
        /// into a NEW track (the source is never modified). Afterwards the
        /// source's edits are cleared and it is muted, so the mix sounds the
        /// same as the preview did — one undo step brings everything back.
        /// </summary>
        private void saveEdits(ForgeTrack source, string name)
        {
            if (!source.IsShaped)
            {
                rowFor(source)?.ShowEditHint("Nothing to save yet: add a range edit, a filter, or trim the track's start or end.");
                return;
            }

            var problem = source.EffectiveEdits().Validate().FirstOrDefault(i => i.IsError);
            if (problem.Message != null)
            {
                rowFor(source)?.ShowEditHint(problem.Message);
                return;
            }

            string trackName = string.IsNullOrWhiteSpace(name) ? $"{source.Name} (edit)" : name.Trim();

            setBusy(true);
            setWorking(source, $"saving '{trackName}'…");

            string outWav = ForgeTrackSession.NewWorkPath("edit");

            runJob(() => ForgeJobs.RenderTrackAsync(source, outWav, Scheduler),
                null,
                startMs =>
                {
                    history.Record();

                    var created = trackSet.Import(trackName, outWav);
                    if (created == null)
                    {
                        rowFor(source)?.ShowEditHint($"'{trackName}' is already in the set.");
                        return;
                    }

                    created.IsSeparated = source.IsSeparated;
                    created.IsMixdown = source.IsMixdown;
                    created.Gain = source.Gain;
                    created.OffsetMs = startMs;
                    history.MarkCreated(created);

                    source.Edits = new TrackEdits();
                    source.ClipStartMs = 0;
                    source.ClipEndMs = 0;
                    source.Muted = true;

                    rebuildRows();
                    analyzeLater(created);
                    ForgeNotify.Info(notifications,
                        $"Saved '{trackName}' as a new track. '{source.Name}' was muted so the mix sounds the same (Ctrl+Z undoes it all).",
                        FontAwesome.Solid.Save);
                },
                ex =>
                {
                    rowFor(source)?.ShowEditHint($"Saving failed: {ex.Message}");
                    ForgeNotify.Error(notifications, $"Saving the edits of '{source.Name}' failed: {ex.Message}");
                });
        }

        // ------------------------------------------------------------------
        // Toolbar, undo/redo, save, level set
        // ------------------------------------------------------------------

        private Drawable createToolbar()
        {
            ForgeButton button(IconUsage icon, string text, string tooltip, Action action, float width) => new ForgeButton(icon, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = width,
                Height = 30,
                TooltipText = tooltip,
                Action = action,
            };

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new osuTK.Vector2(6),
                Children = new Drawable[]
                {
                    undoButton = button(FontAwesome.Solid.Undo, @"undo", @"Undo the last change on this tab (Ctrl+Z)", undo, 90),
                    redoButton = button(FontAwesome.Solid.Redo, @"redo", @"Redo (Ctrl+Y or Ctrl+Shift+Z)", redo, 90),
                    new Container { Width = 12, Height = 30 },
                    button(FontAwesome.Solid.SearchMinus, @"zoom out", @"Show more time (or Ctrl+wheel on a track)",
                        () => monitor.ZoomBy(2, monitor.ViewStartMs + monitor.ViewLengthMs / 2), 110),
                    button(FontAwesome.Solid.SearchPlus, @"zoom in", @"Show more detail around the middle of the view (or Ctrl+wheel on a track)",
                        () => monitor.ZoomBy(0.5, monitor.ViewStartMs + monitor.ViewLengthMs / 2), 100),
                    button(FontAwesome.Solid.ExpandArrowsAlt, @"fit", @"Show the whole set", monitor.Fit, 80),
                    new Container { Width = 12, Height = 30 },
                    clearSoloButton = button(FontAwesome.Solid.Headphones, @"clear solo", @"You're hearing only the soloed tracks — click to hear every track again", () =>
                    {
                        monitor.ClearSolo();
                        onRoleChanged();
                    }, 150),
                    button(FontAwesome.Solid.Save, @"save", @"Save the set and the level (Ctrl+S). The set is also saved automatically.", saveAll, 90),
                },
            };
        }

        /// <summary>Arrange (lanes, editing, actions) | Mix the companion (the level audio's mixing desk). Same play bar for both.</summary>
        private Drawable createViewSwitch() => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new osuTK.Vector2(6, 0),
            Margin = new MarginPadding { Left = 9 },
            Children = new Drawable[]
            {
                arrangeTab = new ViewTab(FontAwesome.Solid.Stream, @"Arrange", @"Place, edit and process the tracks", () => showView(false)) { Selected = true },
                mixTab = new ViewTab(FontAwesome.Solid.SlidersH, @"Mix the companion", @"Choose and balance the companion tracks: their mix is the level audio", () => showView(true)),
            },
        };

        private void showView(bool mix)
        {
            arrangeTab.Selected = !mix;
            mixTab.Selected = mix;
            // Hidden views take no space (not present in the flow).
            arrangeView.Alpha = mix ? 0 : 1;
            mixView.Alpha = mix ? 1 : 0;

            // The mix view is about the final track: hear it there by default.
            if (mix)
                mixView.EnterView();
            else
                mixView.LeaveView();
        }

        private void undo() => applyHistory(history.Undo);

        private void redo() => applyHistory(history.Redo);

        private void applyHistory(Func<bool> step)
        {
            if (!step()) return;

            rebuildRows();
        }

        /// <summary>Rebuild every row from the model (after undo/redo or a save, so sliders and lists re-read it).</summary>
        private void rebuildRows()
        {
            everDrawn = false;
            syncRows();
        }

        private void saveAll()
        {
            updateLevelAudioIfChanged();

            if (ForgeTrackSession.Save(out string? error))
            {
                BeatmapAudio.SaveEditor(editor);
                ForgeNotify.Info(notifications, $"Saved the level and its set ({ForgeFormat.Plural(trackSet.Tracks.Count, "track", "tracks")}).", FontAwesome.Solid.Save);
            }
            else
                ForgeNotify.Error(notifications, $"Could not save the set: {error}");
        }

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            if (!IsPresent) return false;

            switch (e.Action)
            {
                case PlatformAction.Undo:
                    undo();
                    return true; // the beatmap's own undo stays untouched on this tab

                case PlatformAction.Redo:
                    redo();
                    return true;

                case PlatformAction.Save:
                    updateLevelAudioIfChanged();
                    ForgeTrackSession.Save();
                    return false; // let the editor save the level too
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        /// <summary>Load the open level's saved set (no-op if already open) and re-analyse what came back.</summary>
        private void openLevelSet()
        {
            foreach (var track in LevelSession.Open(storage, working.Value))
                analyzeLater(track);
        }

        public override void OnExiting(ScreenExitEvent e)
        {
            base.OnExiting(e);
            ForgeTrackSession.Save();
        }

        // ------------------------------------------------------------------
        // Optional actions — none of them requires another
        // ------------------------------------------------------------------

        private void startDemux()
        {
            if (!KanForgeNative.HasSeparator())
            {
                const string unavailable = "Demux is unavailable in this build: the compiled RoFormer runtime (libkan) is missing.";
                demuxCard.Fail(unavailable);
                ForgeNotify.Error(notifications, unavailable);
                return;
            }

            var queue = trackSet.DemuxQueue.ToList();
            if (queue.Count == 0)
            {
                demuxCard.Hint(trackSet.Tracks.Count == 0
                    ? "Nothing to separate yet — import a track in Resources, then give it the demux chip."
                    : "Give at least one track the demux chip (separated stems and mixdowns can't be demuxed).");
                return;
            }

            history.Record();
            setBusy(true);
            demuxCard.Progress(0, "starting");

            runJob(() => Task.Run(() => demuxAll(queue)),
                demuxCard,
                stemsAdded => Scheduler.Add(() =>
                {
                    // Queued after the stems' own additions (same scheduler).
                    // Meaningful stems first; "no meaningful audio" stems go
                    // to the end of the list so they don't mislead.
                    trackSet.OrderByMeaning();
                    setWorking(null, null);
                    string done = $"Demux done — {ForgeFormat.Plural(stemsAdded, "stem", "stems")} joined the set.";
                    demuxCard.Complete(done);
                    ForgeNotify.Info(notifications, done, FontAwesome.Solid.CodeBranch);
                }),
                ex =>
                {
                    setWorking(null, null);
                    demuxCard.Fail($"Demux failed: {ex.Message}");
                    ForgeNotify.Error(notifications, $"Demux failed: {ex.Message}");
                });
        }

        private int demuxAll(List<ForgeTrack> queue)
        {
            int stemsAdded = 0;

            for (int i = 0; i < queue.Count; i++)
            {
                var track = queue[i];
                string outDir = ForgeTrackSession.NewWorkPath("stems", string.Empty);

                string context = queue.Count > 1 ? $"{track.Name} ({i + 1}/{queue.Count})" : track.Name;
                Scheduler.Add(() =>
                {
                    demuxCard.SetContext(context);
                    demuxCard.Progress(0, "demux");
                    setWorking(track, "demuxing…");
                });
                ForgeJobs.SeparateStemsAsync(track.Path, outDir, report(demuxCard), Scheduler)
                         .GetAwaiter().GetResult();

                // Stems are built and analysed here (decoding is slow), but the
                // shared set is only touched on the update thread: the UI reads
                // it every frame and must never see it mid-change.
                var stems = new List<ForgeTrack>();

                if (Directory.Exists(outDir))
                {
                    foreach (string file in Directory.EnumerateFiles(Path.Combine(outDir, "roformer"), "*.wav")
                                                     .OrderBy(f => f))
                    {
                        var stem = new ForgeTrack(Path.GetFileNameWithoutExtension(file), file)
                        {
                            IsSeparated = true,
                            OffsetMs = track.OffsetMs, // stems share their source's timing
                        };

                        // Label stems that seem to carry no meaningful audio.
                        TrackAnalysis.Analyze(stem);
                        stems.Add(stem);
                    }
                }

                stemsAdded += stems.Count;

                Scheduler.Add(() =>
                {
                    // The source has been demuxed (never again), and the
                    // results join the list as separated tracks.
                    track.DemuxPending = false;
                    track.IsSeparated = true;

                    foreach (var stem in stems)
                    {
                        if (trackSet.ContainsPath(stem.Path)) continue;

                        trackSet.Add(stem);
                        history.MarkCreated(stem);
                    }
                });
            }

            return stemsAdded;
        }

        private void startTranscription()
        {
            var reference = trackSet.Reference;
            if (reference == null)
            {
                transcribeCard.Hint("Give one track the reference chip — transcription uses it automatically.");
                return;
            }

            setBusy(true);
            transcribeCard.SetContext(reference.Name);
            transcribeCard.Progress(0, "starting");
            setWorking(reference, "transcribing…");

            string instrument = ForgeTrackSession.Instrument;
            runJob(() => ForgeJobs.TranscribeAsync(reference.Path, instrument, report(transcribeCard), Scheduler),
                transcribeCard,
                draft =>
                {
                    setWorking(null, null);

                    // Kept with the set and drawn on the reference's lane.
                    ForgeTrackSession.Draft = draft;
                    ForgeTrackSession.DraftSource = reference.Path;
                    ForgeTrackSession.DraftAtUtc = DateTime.UtcNow;
                    ForgeTrackSession.Save();

                    string done = $"Transcription ready from '{reference.Name}': {describe(draft)} — shown on its track and offered in the compose tab.";
                    transcribeCard.Complete(done);
                    ForgeNotify.Info(notifications, done, FontAwesome.Solid.Microphone);
                },
                ex =>
                {
                    setWorking(null, null);
                    transcribeCard.Fail($"Transcription failed: {ex.Message}");
                    ForgeNotify.Error(notifications, $"Transcription failed: {ex.Message}");
                });
        }

        /// <summary>Bring the level audio file up to date if the companion mix changed (leaving the tab, saving).</summary>
        private void updateLevelAudioIfChanged()
        {
            if (trackSet.Companions.Any() && !BeatmapAudio.IsUpToDate(trackSet) && !BeatmapAudio.Rendering)
                renderLevelAudio(true);
        }

        /// <summary>
        /// Render the level audio (the companion mix, as heard) and install it.
        /// <paramref name="automatic"/>: started by leaving the tab, so a
        /// missing selection is not worth a hint.
        /// </summary>
        private void renderLevelAudio(bool automatic)
        {
            int companions = trackSet.Companions.Count();

            if (companions == 0)
            {
                if (!automatic)
                    renderCard.Hint("Mark at least one audible track as companion — the level audio is their mix.");
                return;
            }

            if (BeatmapAudio.Rendering) return;

            string what = ForgeFormat.Plural(companions, "companion track", "companion tracks");

            setBusy(true);
            renderCard.Indeterminate($"mixing {what}");

            runJob(() => BeatmapAudio.RenderAsync(trackSet, working.Value, beatmaps, music, editor, Scheduler),
                renderCard,
                name =>
                {
                    string done = automatic
                        ? $"The level audio is up to date: \"{name}\", mixed from {what}."
                        : $"Rendered the level audio: \"{name}\", mixed from {what}.";
                    renderCard.Complete(done);
                    ForgeNotify.Info(notifications, done, FontAwesome.Solid.LayerGroup);
                },
                ex =>
                {
                    renderCard.Fail($"Rendering the level audio failed: {ex.Message}");
                    ForgeNotify.Error(notifications, $"Rendering the level audio failed: {ex.Message}");
                });
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        /// <summary>
        /// Run a job; the callbacks report the outcome on the card that
        /// started it (and as a notification). Edits have no card: their row
        /// shows the work in place.
        /// </summary>
        private async void runJob<T>(Func<Task<T>> body, KanActionCard? card,
                                     Action<T> onDone, Action<Exception> onFail)
        {
            try
            {
                T result = await body();
                onDone(result);
            }
            catch (Exception ex)
            {
                onFail(ex);
            }
            finally
            {
                if (card == null)
                    setWorking(null, null);

                setBusy(false);
            }
        }

        private static Action<ForgeProgress> report(KanActionCard card)
            => p => card.Progress(p.Percent, p.Phase);

        private void setBusy(bool busy)
        {
            demuxCard.Enabled.Value = !busy;
            transcribeCard.Enabled.Value = !busy;
            renderCard.Enabled.Value = !busy;
        }

        private static string describe(TranscriptionDraft draft)
        {
            if (draft.Notes.Count == 0)
                return @"no resolved notes (empty draft — check the source audio)";

            int unresolved = 0;
            foreach (var n in draft.Notes)
                if (n.Midi < 0) unresolved++;

            return $"{draft.Notes.Count} notes ({unresolved} unresolved pitch)";
        }

        /// <summary>A view switch tab (selected = filled).</summary>
        private partial class ViewTab : OsuClickableContainer
        {
            private readonly IconUsage icon;
            private readonly string text;
            private readonly string tooltip;

            private Box fill = null!;
            private bool selected;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            public ViewTab(IconUsage icon, string text, string tooltip, Action select)
            {
                this.icon = icon;
                this.text = text;
                this.tooltip = tooltip;
                Action = select;
                AutoSizeAxes = Axes.X;
                Height = 34;
            }

            public override osu.Framework.Localisation.LocalisableString TooltipText
            {
                get => tooltip;
                set { }
            }

            public bool Selected
            {
                get => selected;
                set
                {
                    selected = value;
                    if (IsLoaded) updateLook();
                }
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 6;

                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Horizontal,
                        Spacing = new osuTK.Vector2(8, 0),
                        Padding = new MarginPadding { Horizontal = 14 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon { Icon = icon, Size = new osuTK.Vector2(13), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            new OsuSpriteText { Text = text, Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        },
                    },
                };

                updateLook();
            }

            private void updateLook()
            {
                fill.Colour = selected ? colourProvider.Background2 : colourProvider.Background5;
                Alpha = selected ? 1 : 0.75f;
            }
        }
    }
}
