// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;
using Kan.Core.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// kan!'s compose tab (fork F-13): the score editor.
    /// The score — the notes gameplay judges (timing, pitch, duration,
    /// intensity, playing style) — is created, adjusted and improved here
    /// on a piano roll snapped to the timing tab's beat grid. It is saved
    /// inside the level (<see cref="KanScore.FILE_NAME"/>).
    /// </summary>
    public partial class KanComposeScreen : EditorScreen, IKeyBindingHandler<PlatformAction>, SectionPanel.ITools
    {
        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private BindableBeatDivisor beatDivisor { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private PianoRoll roll = null!;
        private KeyAudition audition = null!;
        private Box sectionShade = null!;
        private ComposeMonitor monitor = null!;
        private SpectrogramLayer companionLayer = null!, referenceLayer = null!;
        private ForgeButton companionToggle = null!, referenceToggle = null!, notesToggle = null!;
        private NoteInspector inspector = null!;
        private ForgeButton selectTool = null!, drawTool = null!, snapButton = null!, undoButton = null!, redoButton = null!;
        private OsuSpriteText status = null!;
        private GridTempoBar gridTempo = null!;
        private ShortcutsPanel shortcuts = null!;
        private readonly BindableDouble transcriberDelay = new BindableDouble();
        private Container emptyState = null!;
        private Container newerTranscription = null!;
        private OsuSpriteText newerTranscriptionText = null!;

        private bool dirty;
        private int autosaveCountdown;

        public KanComposeScreen()
            : base(EditorScreenMode.Compose)
        {
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            // The level's track set (for the transcription), its score and design.
            LevelSession.Open(storage, working.Value);

            Add(audition = new KeyAudition());

            roll = new PianoRoll
            {
                BeforeChange = () => record(null),
                Changed = changed,
                KeyPlayed = midi => audition.Play(midi),
            };
            inspector = new NoteInspector(roll, k => record(k), changed, new SectionPanel(this, changed));

            Add(monitor = new ComposeMonitor());
            roll.Overlays.AddRange(new Drawable[]
            {
                sectionShade = new Box { Colour = colours.Red1, Alpha = 0 },
                companionLayer = new SpectrogramLayer(roll, ForgeRoles.Colour(colours, ForgeRole.Companion)) { RelativeSizeAxes = Axes.Both },
                referenceLayer = new SpectrogramLayer(roll, ForgeRoles.Colour(colours, ForgeRole.Reference)) { RelativeSizeAxes = Axes.Both },
            });

            Add(new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 });
            Add(new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize), new Dimension() },
                Content = new[]
                {
                    new Drawable[] { createToolbar(colourProvider) },
                    new Drawable[]
                    {
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 10, Bottom = 10 },
                            ColumnDimensions = new[]
                            {
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, 8),
                                new Dimension(GridSizeMode.AutoSize),
                            },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Masking = true,
                                        CornerRadius = 6,
                                        Children = new Drawable[]
                                        {
                                            roll,
                                            emptyState = createEmptyState(colourProvider),
                                            newerTranscription = createNewerTranscriptionBanner(colourProvider),
                                            shortcuts = new ShortcutsPanel(),
                                        },
                                    },
                                    Empty(),
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Y,
                                        AutoSizeAxes = Axes.X,
                                        Masking = true,
                                        CornerRadius = 6,
                                        Child = inspector,
                                    },
                                },
                            },
                        },
                    },
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            roll.RequestFit();
            TimelineRepair.Restored += repairRestored;

            // A new transcriber delay moves every note by the difference (owner 2026-10-14:
            // the transcribed notes all suffer the same lateness).
            transcriberDelay.BindTo(ForgeTiming.TRANSCRIBER_DELAY_MS);
            transcriberDelay.BindValueChanged(v => shiftForTranscriberDelay(v.NewValue - v.OldValue));
        }

        private void shiftForTranscriberDelay(double deltaMs)
        {
            if (deltaMs == 0 || ScoreSession.Score.Notes.Count == 0) return;

            record("transcriber-delay");
            ScoreSession.Score.Move(ScoreSession.Score.Notes, -deltaMs, 0);
            ScoreSession.Score.Sort();
            roll.Invalidate();
            changed();
        }

        [Resolved]
        private osu.Game.OsuGame? game { get; set; }

        private Kan.Forge.Play.MicTimingOverlay? micTiming;

        /// <summary>The mic delay measurement (the player's; the same as before playing a level).</summary>
        public void ShowMicCalibration()
        {
            if (game == null) return;

            shortcuts.Hide();
            clock.Stop();

            if (micTiming == null)
                game.Add(micTiming = new Kan.Forge.Play.MicTimingOverlay(() => ForgeTrackSession.Instrument));

            micTiming.Show();
        }

        protected override void Dispose(bool isDisposing)
        {
            // Leaving the editor: the measurement goes with it.
            micTiming?.Expire();
            TimelineRepair.Restored -= repairRestored;
            if (grid != null) grid.Gone = true;
            base.Dispose(isDisposing);
        }

        protected override void Update()
        {
            base.Update();

            followGrid();

            undoButton.Enabled.Value = ScoreSession.History.CanUndo;
            redoButton.Enabled.Value = ScoreSession.History.CanRedo;
            selectTool.Alpha = roll.Tool == RollTool.Select ? 1 : 0.55f;
            drawTool.Alpha = roll.Tool == RollTool.Draw ? 1 : 0.55f;
            snapButton.Alpha = roll.Snap ? 1 : 0.55f;
            CanCut.Value = CanCopy.Value = roll.Selected.Count > 0;
            CanPaste.Value = clipboard.Count > 0;

            emptyState.Alpha = ScoreSession.Score.Notes.Count == 0 ? 1 : 0;

            bool newer = ForgeTrackSession.Draft is { Notes.Count: > 0 } && ForgeTrackSession.DraftAtUtc > ScoreSession.Score.TranscriptionAtUtc
                                                                   && ScoreSession.Score.Notes.Count > 0;
            newerTranscription.Alpha = newer ? 1 : 0;
            if (newer)
                newerTranscriptionText.Text = $"A newer transcription is available ({ForgeFormat.Plural(ForgeTrackSession.Draft!.Notes.Count, "note", "notes")})";

            // The toggles drive both what is heard and what is drawn.
            bool hasReference = monitor.Reference != null;
            referenceToggle.Enabled.Value = hasReference;
            companionToggle.Alpha = monitor.CompanionOn.Value ? 1 : 0.55f;
            referenceToggle.Alpha = monitor.ReferenceOn.Value && hasReference ? 1 : 0.55f;
            notesToggle.Alpha = monitor.NotesOn.Value ? 1 : 0.55f;
            companionLayer.Alpha = monitor.CompanionOn.Value ? 1 : 0;
            referenceLayer.Alpha = monitor.ReferenceOn.Value && hasReference ? 1 : 0;
            if (monitor.Reference is { } reference)
                referenceLayer.OffsetMs = ComposeMonitor.ReferenceOffsetMs(reference);

            int count = ScoreSession.Score.Notes.Count;
            status.Text = $"{ForgeFormat.Plural(count, "note", "notes")}"
                          + (roll.Selected.Count > 0 ? $" · {roll.Selected.Count} selected" : string.Empty)
                          + (ScoreSession.SavedInLevel ? " · saved in the level" : " · not saved in the level yet");

            // The section, shaded behind the notes.
            if (HasSection)
            {
                float x0 = roll.XOf(SectionFromMs), x1 = roll.XOf(SectionToMs);
                sectionShade.X = x0;
                sectionShade.Width = Math.Max(1, x1 - x0);
                sectionShade.Height = roll.DrawHeight;
                sectionShade.Alpha = Recording ? 0.16f : 0.08f;
            }
            else
                sectionShade.Alpha = 0;

            if (Recording && (clock.CurrentTime >= SectionToMs + 300 || !clock.IsRunning))
                finishRecording();

            if (dirty && --autosaveCountdown <= 0)
            {
                dirty = false;
                ScoreSession.AutoSave();
            }
        }

        protected override void PopOut()
        {
            base.PopOut();

            // osu!framework also calls this once while the screen is still
            // loading, on the loading thread: nothing to stop or save yet.
            if (!IsLoaded) return;

            monitor.Active.Value = false;
            saveToLevel(false);
        }

        protected override void PopIn()
        {
            base.PopIn();
            monitor.Active.Value = true;
            loadSpectrograms();

            if (ScoreSession.RemovedBrokenNotes > 0)
            {
                ForgeNotify.Info(notifications,
                    $"Removed {ForgeFormat.Plural(ScoreSession.RemovedBrokenNotes, "broken note", "broken notes")} left by a transcription bug (now fixed). Run Transcribe again in the track tab to get the real notes.",
                    FontAwesome.Solid.Microphone);
                ScoreSession.RemovedBrokenNotes = 0;
            }

            // A transcription made in the track tab starts an empty score by itself.
            if (ScoreSession.Score.Notes.Count == 0 && ForgeTrackSession.Draft is { Notes.Count: > 0 })
                startFromTranscription();
        }

        /// <summary>(Re)compute the spectrograms when their sources changed (the level audio is re-rendered, the reference changes).</summary>
        private void loadSpectrograms()
        {
            var file = working.Value.BeatmapSetInfo.GetFile(working.Value.Metadata.AudioFile ?? string.Empty);

            if (file != null)
            {
                string storagePath = file.File.GetStoragePath();
                companionLayer.Load(file.File.Hash, () =>
                {
                    using var stream = working.Value.GetStream(storagePath);
                    if (stream == null) return null;

                    using var memory = new System.IO.MemoryStream();
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }, 0);
            }
            else
                companionLayer.Clear();

            if (monitor.Reference is { } reference && System.IO.File.Exists(reference.Path))
            {
                string path = reference.Path;
                referenceLayer.Load(path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks,
                    () => System.IO.File.ReadAllBytes(path), ComposeMonitor.ReferenceOffsetMs(reference));
            }
            else
                referenceLayer.Clear();
        }

        public override void OnExiting(ScreenExitEvent e)
        {
            base.OnExiting(e);
            saveToLevel(false);
        }

        private void startFromTranscription()
        {
            var draft = ForgeTrackSession.Draft;

            if (draft == null || draft.Notes.Count == 0)
            {
                ForgeNotify.Info(notifications, "There is no transcription yet: in the track tab, pick the reference in the Transcribe card and run it.", FontAwesome.Solid.Microphone);
                return;
            }

            // The transcription is in the reference file's own time. Notes live on the
            // level audio's timeline, which begins at the level audio's start on the set.
            var source = ForgeTrackSession.Tracks.Tracks.FirstOrDefault(t => string.Equals(t.Path, ForgeTrackSession.DraftSource, StringComparison.OrdinalIgnoreCase));
            double offset = (source?.OffsetMs ?? 0) - ForgeTrackSession.Tracks.AudioStartMs - ForgeTiming.TRANSCRIBER_DELAY_MS.Value;

            record(null);
            bool replacing = ScoreSession.Score.Notes.Count > 0;
            var fromDraft = KanScore.FromDraft(draft, ForgeTrackSession.Instrument, offset);
            fromDraft.TranscriptionAtUtc = ForgeTrackSession.DraftAtUtc;
            ScoreSession.Replace(fromDraft);
            roll.Selected.Clear();
            roll.Invalidate();
            roll.RequestFit();
            changed();

            ForgeNotify.Info(notifications,
                $"The score now starts from the transcription ({ForgeFormat.Plural(ScoreSession.Score.Notes.Count, "note", "notes")})."
                + (replacing ? " Ctrl+Z brings the previous score back." : string.Empty),
                FontAwesome.Solid.Microphone);
        }
    }
}
