// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
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
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;
using Kan.Forge.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// kan!'s compose tab (owner 2026-10-14, fork F-13): the score editor.
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
        private GameHost host { get; set; } = null!;

        private PianoRoll roll = null!;
        private Box sectionShade = null!;
        private ComposeMonitor monitor = null!;
        private SpectrogramLayer companionLayer = null!, referenceLayer = null!;
        private ForgeButton companionToggle = null!, referenceToggle = null!, notesToggle = null!;
        private NoteInspector inspector = null!;
        private ForgeButton selectTool = null!, drawTool = null!, snapButton = null!, undoButton = null!, redoButton = null!;
        private OsuSpriteText status = null!;
        private GridTempoBar gridTempo = null!;
        private ShortcutsPanel shortcuts = null!;
        private MicCalibrationPanel calibration = null!;
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
            // The level's track set (for the transcription) and its score.
            string key = working.Value.BeatmapSetInfo.ID.ToString("N");
            ForgeTrackSession.Open(storage.GetFullPath(@"kan-forge"), key);
            ScoreSession.Open(working.Value, key);

            roll = new PianoRoll
            {
                BeforeChange = () => record(null),
                Changed = changed,
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
                                            calibration = new MicCalibrationPanel(),
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

        public void ShowMicCalibration()
        {
            shortcuts.Hide();
            calibration.Show();
        }

        // ------------------------------------------------------------------
        // Toolbar
        // ------------------------------------------------------------------

        private Drawable createToolbar(OverlayColourProvider colourProvider)
        {
            ForgeButton button(IconUsage icon, string text, string tooltip, Action action, float width) => new ForgeButton(icon, text)
            {
                RelativeSizeAxes = Axes.None,
                Width = width,
                Height = 30,
                TooltipText = tooltip,
                Action = action,
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding(10),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(6),
                        Children = new Drawable[]
                        {
                            selectTool = button(FontAwesome.Solid.MousePointer, @"select", @"Select, move and resize notes (drag a box to select several) — S", () => setTool(RollTool.Select), 96),
                            drawTool = button(FontAwesome.Solid.PencilAlt, @"draw", @"Click to add a note, drag to set its length — D", () => setTool(RollTool.Draw), 90),
                            new Container { Width = 8, Height = 30 },
                            snapButton = button(FontAwesome.Solid.Magnet, @"snap", @"Snap notes to the beat grid (hold Shift while dragging to place freely) — N", toggleSnap, 86),
                            new Container { Width = 8, Height = 30 },
                            companionToggle = button(FontAwesome.Solid.LayerGroup, @"companion", @"Hear and see the companion (the level audio) behind the notes — M", () => monitor.CompanionOn.Toggle(), 128),
                            referenceToggle = button(FontAwesome.Solid.Microphone, @"reference", @"Hear and see the reference (the full performance) behind the notes — R", () => monitor.ReferenceOn.Toggle(), 124),
                            notesToggle = button(FontAwesome.Solid.Music, @"notes", @"Hear the score itself, in time with the rest: to place notes by ear when there is no reference — P", () => monitor.NotesOn.Toggle(), 96),
                            new Container { Width = 8, Height = 30 },
                            undoButton = button(FontAwesome.Solid.Undo, @"undo", @"Undo (Ctrl+Z)", undo, 88),
                            redoButton = button(FontAwesome.Solid.Redo, @"redo", @"Redo (Ctrl+Y)", redo, 88),
                            new Container { Width = 8, Height = 30 },
                            button(FontAwesome.Solid.SearchMinus, @"zoom out", @"Show more time (or Ctrl+wheel) — minus key", () => roll.ZoomTime(2, roll.ViewStartMs + roll.ViewLengthMs / 2), 108),
                            button(FontAwesome.Solid.SearchPlus, @"zoom in", @"Show more detail (or Ctrl+wheel; Ctrl+Shift+wheel for pitch) — plus key", () => roll.ZoomTime(0.5, roll.ViewStartMs + roll.ViewLengthMs / 2), 98),
                            button(FontAwesome.Solid.ExpandArrowsAlt, @"fit", @"Show the whole score — F", roll.Fit, 72),
                            new Container { Width = 8, Height = 30 },
                            button(FontAwesome.Solid.Microphone, @"start from the transcription", @"Use the transcription of the reference (track tab → Transcribe) as the starting score", startFromTranscription, 230),
                            button(FontAwesome.Solid.Clone, @"remove repeats", @"Remove notes the transcriber heard twice: copies of a note at the same moment — Shift + U (Ctrl+Z brings them back)", removeRepeats, 150),
                            button(FontAwesome.Solid.Save, @"save", @"Save the score into the level (Ctrl+S). It is also saved when you leave this tab.", () => saveToLevel(true), 86),
                            button(FontAwesome.Solid.FileExport, @"export MIDI", @"Save the corrected score as MIDI next to a copy of the reference audio (and the automatic transcription), to check the transcriber against later", exportForValidation, 124),
                            button(FontAwesome.Solid.Keyboard, @"keys", @"Keyboard shortcuts (K)", () => shortcuts.ToggleVisibility(), 76),
                            gridTempo = new GridTempoBar { Margin = new MarginPadding { Top = 2 } },
                        },
                    },
                    status = new OsuSpriteText
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Font = OsuFont.Style.Caption2,
                        Colour = colourProvider.Foreground1,
                        Y = 4,
                    },
                },
            };
        }

        private Container createEmptyState(OverlayColourProvider colourProvider) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Child = new FillFlowContainer
            {
                Width = 460,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    new OsuSpriteText { Text = @"No notes yet", Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold), Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
                    new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.Style.Caption1;
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        TextAnchor = Anchor.TopCentre,
                        Text = "The score is what gameplay judges: each note's timing, pitch, duration, intensity and playing style.\n"
                               + "Start from the transcription of the reference (track tab → Transcribe), or pick the draw tool and click to add notes.",
                    },
                    new ForgeButton(FontAwesome.Solid.Microphone, @"start from the transcription")
                    {
                        RelativeSizeAxes = Axes.None,
                        Width = 260,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Action = startFromTranscription,
                    },
                },
            },
        };

        /// <summary>A newer transcription than the one the score started from (track tab → Transcribe).</summary>
        private Container createNewerTranscriptionBanner(OverlayColourProvider colourProvider) => new Container
        {
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Margin = new MarginPadding { Top = 8 },
            Masking = true,
            CornerRadius = 6,
            Alpha = 0,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Padding = new MarginPadding(8),
                    Children = new Drawable[]
                    {
                        new SpriteIcon { Icon = FontAwesome.Solid.Microphone, Size = new Vector2(14), Colour = ForgeRoles.Colour(colours, ForgeRole.Reference), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        newerTranscriptionText = new OsuSpriteText { Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold), Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        new ForgeButton(FontAwesome.Solid.Sync, @"use it")
                        {
                            RelativeSizeAxes = Axes.None,
                            Width = 90,
                            Height = 28,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            TooltipText = @"Replace the score with the new transcription (Ctrl+Z brings the current score back)",
                            Action = startFromTranscription,
                        },
                    },
                },
            },
        };

        private void setTool(RollTool tool)
        {
            roll.Tool = tool;
        }

        private void toggleSnap() => roll.Snap = !roll.Snap;

        protected override void Update()
        {
            base.Update();

            undoButton.Enabled.Value = ScoreSession.History.CanUndo;
            redoButton.Enabled.Value = ScoreSession.History.CanRedo;
            selectTool.Alpha = roll.Tool == RollTool.Select ? 1 : 0.55f;
            drawTool.Alpha = roll.Tool == RollTool.Draw ? 1 : 0.55f;
            snapButton.Alpha = roll.Snap ? 1 : 0.55f;

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

        // ------------------------------------------------------------------
        // Changes, undo, save
        // ------------------------------------------------------------------

        private void record(string? key) => ScoreSession.History.Record(ScoreSession.Score, key);

        private void changed()
        {
            dirty = true;
            autosaveCountdown = 60;
            inspector.Invalidate();
            monitor.NotesChanged();
        }

        private void undo() => applyHistory(ScoreSession.History.Undo(ScoreSession.Score));

        private void redo() => applyHistory(ScoreSession.History.Redo(ScoreSession.Score));

        private void applyHistory(KanScore? score)
        {
            if (score == null) return;

            ScoreSession.Replace(score);
            roll.Selected.Clear();
            roll.Invalidate();
            changed();
        }

        private void saveToLevel(bool notify)
        {
            if (ScoreSession.SavedInLevel && !notify) return;

            string? error = ScoreSession.SaveToLevel(working.Value, beatmaps);

            if (error != null)
                ForgeNotify.Error(notifications, $"Could not save the score into the level: {error}");
            else if (notify)
                ForgeNotify.Info(notifications, $"Saved the score into the level ({ForgeFormat.Plural(ScoreSession.Score.Notes.Count, "note", "notes")}).", FontAwesome.Solid.Save);
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

        // ------------------------------------------------------------------
        // Validation export (owner 2026-10-14)
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes a folder that pairs audio with ground truth: the reference
        /// audio, the corrected score and the automatic transcription — both
        /// MIDI files in the reference file's own time, so they line up with
        /// it in any MIDI/audio tool and in tools/dataset_bench.
        /// </summary>
        private void exportForValidation()
        {
            var score = ScoreSession.Score;

            if (score.Notes.Count == 0)
            {
                ForgeNotify.Info(notifications, "There are no notes to export yet.", FontAwesome.Solid.FileExport);
                return;
            }

            saveToLevel(false);

            try
            {
                var reference = ForgeTrackSession.Tracks.Reference;
                bool hasReference = reference != null && System.IO.File.Exists(reference.Path);

                // The reference file's time (it starts at this point of the level audio's timeline).
                double shift = hasReference ? -ComposeMonitor.ReferenceOffsetMs(reference!) : 0;

                string title = string.Concat((working.Value.Metadata.Title is { Length: > 0 } t ? t : @"level").Split(System.IO.Path.GetInvalidFileNameChars()));
                string folder = System.IO.Path.Combine(ForgeTrackSession.WorkDir, @"exports", $"{title} {DateTime.Now:yyyy-MM-dd HHmmss}");
                System.IO.Directory.CreateDirectory(folder);

                var tp = EditorBeatmap.ControlPointInfo.TimingPoints.FirstOrDefault();
                double beatLength = tp?.BeatLength ?? 500;
                int beatsPerBar = tp?.TimeSignature.Numerator ?? 4;

                var known = score.Notes.Where(n => !n.PitchUnknown).ToList();
                int unknown = score.Notes.Count - known.Count;
                var scoreNotes = known.Select(n => new MidiExport.Note(n.StartMs + shift, n.DurationMs, n.Midi, MidiExport.Velocity(n.Intensity))).ToList();
                int outside = scoreNotes.Count(n => n.StartMs < 0);
                MidiExport.WriteFile(System.IO.Path.Combine(folder, @"score (corrected).mid"), scoreNotes, $"{title} — corrected score", beatLength, beatsPerBar);

                var about = new System.Text.StringBuilder();
                about.AppendLine($"kan! validation export — {title}");
                about.AppendLine($"Exported {DateTime.Now:yyyy-MM-dd HH:mm:ss}. Instrument: {Kan.Forge.Pipeline.ForgeJobs.ProfileLabel(ForgeTrackSession.Instrument)}.");
                about.AppendLine();
                about.AppendLine($"score (corrected).mid — the score as corrected in the compose tab: {scoreNotes.Count - outside} notes (ground truth).");
                if (unknown > 0) about.AppendLine($"  {unknown} notes with an unknown pitch (?) were left out: fix or delete them, then export again.");
                if (outside > 0) about.AppendLine($"  {outside} notes before the reference's first sample were left out.");

                if (hasReference)
                {
                    string audioName = @"reference" + System.IO.Path.GetExtension(reference!.Path);
                    System.IO.File.Copy(reference.Path, System.IO.Path.Combine(folder, audioName));
                    about.AppendLine($"{audioName} — the reference audio ({reference.Name}). Both MIDI files are in its time: 0 ms = its first sample.");

                    // The automatic transcription, untouched, when it was made from this reference.
                    if (ForgeTrackSession.Draft is { Notes.Count: > 0 } draft
                        && string.Equals(ForgeTrackSession.DraftSource, reference.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        var auto = draft.Notes.Where(n => n.Midi >= 0)
                                        .Select(n => new MidiExport.Note(n.OnsetMs, n.DurMs, n.Midi, 100)).ToList();
                        MidiExport.WriteFile(System.IO.Path.Combine(folder, @"transcription (automatic).mid"), auto, $"{title} — automatic transcription", beatLength, beatsPerBar);
                        about.AppendLine($"transcription (automatic).mid — what the transcriber found, before corrections: {auto.Count} notes ({draft.Notes.Count - auto.Count} without a pitch left out).");
                    }
                }
                else
                    about.AppendLine("There is no reference track, so the score is in the level audio's time (0 ms = its start).");

                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, @"about.txt"), about.ToString());

                host.OpenFileExternally(folder);
                ForgeNotify.Info(notifications,
                    $"Exported {ForgeFormat.Plural(scoreNotes.Count - outside, "note", "notes")} to MIDI{(hasReference ? " with the reference audio" : string.Empty)}."
                    + (unknown > 0 ? $" {ForgeFormat.Plural(unknown, "note", "notes")} with an unknown pitch were left out." : string.Empty),
                    FontAwesome.Solid.FileExport);
            }
            catch (Exception ex)
            {
                ForgeNotify.Error(notifications, $"Could not export: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Section tools: record with the mic, re-transcribe from the reference
        // ------------------------------------------------------------------

        public double SectionFromMs { get; private set; } = double.NaN;

        public double SectionToMs { get; private set; } = double.NaN;

        public bool HasSection => !double.IsNaN(SectionFromMs) && !double.IsNaN(SectionToMs) && SectionToMs > SectionFromMs;

        public bool Recording { get; private set; }

        public bool Busy { get; private set; }

        private Kan.Forge.Media.MicRecorder? recorder;
        private double recordingStartMs;

        public void SectionStartHere()
        {
            SectionFromMs = Math.Max(0, clock.CurrentTime);
            if (!double.IsNaN(SectionToMs) && SectionToMs <= SectionFromMs) SectionToMs = double.NaN;
        }

        public void SectionEndHere()
        {
            SectionToMs = Math.Max(0, clock.CurrentTime);
            if (!double.IsNaN(SectionFromMs) && SectionFromMs >= SectionToMs) SectionFromMs = double.NaN;
        }

        public void SectionFromSelection()
        {
            if (roll.Selected.Count == 0)
            {
                ForgeNotify.Info(notifications, "Select some notes first: the section will span them.", FontAwesome.Solid.ObjectGroup);
                return;
            }

            SectionFromMs = roll.Selected.Min(n => n.StartMs);
            SectionToMs = roll.Selected.Max(n => n.EndMs);
        }

        public void ToggleRecording()
        {
            if (Recording)
            {
                finishRecording();
                return;
            }

            if (!HasSection || Busy) return;

            recorder = new Kan.Forge.Media.MicRecorder((SectionToMs - SectionFromMs) / 1000 + 10);

            if (!recorder.Start(out string error))
            {
                recorder.Dispose();
                recorder = null;
                ForgeNotify.Error(notifications, $"Recording failed: {error}");
                return;
            }

            // A short pre-roll, then play the companion along while recording.
            clock.Seek(Math.Max(0, SectionFromMs - 1500));
            recordingStartMs = clock.CurrentTime;
            clock.Start();
            Recording = true;
        }

        private void finishRecording()
        {
            if (!Recording || recorder == null) return;

            Recording = false;
            clock.Stop();

            float[] take = recorder.Stop();
            recorder.Dispose();
            recorder = null;

            if (take.Length < Kan.Forge.Media.MicRecorder.SAMPLE_RATE / 2)
            {
                ForgeNotify.Error(notifications, "Nothing was recorded — check that a microphone is connected and allowed.");
                return;
            }

            string path = ForgeTrackSession.NewWorkPath("mic-take");
            Kan.Forge.Media.MicRecorder.WriteWav(path, take);

            // Sample 0 was captured when the clock stood at the pre-roll start.
            // The mic delay covers the transcriber's lateness too (it is measured through it).
            fillSection(path, recordingStartMs - ForgeTiming.MIC_DELAY_MS.Value, "your recording");
        }

        public void RetranscribeFromReference()
        {
            if (!HasSection || Busy) return;

            var reference = ForgeTrackSession.Tracks.Reference;
            if (reference == null)
            {
                ForgeNotify.Info(notifications, "There is no reference track: choose one in the track tab.", FontAwesome.Solid.Microphone);
                return;
            }

            // The section on the reference file's own timeline.
            double offset = ComposeMonitor.ReferenceOffsetMs(reference);
            double fileFrom = Math.Max(0, SectionFromMs - offset), fileTo = SectionToMs - offset;

            if (fileTo <= fileFrom)
            {
                ForgeNotify.Info(notifications, "The reference has no audio in this section.", FontAwesome.Solid.Microphone);
                return;
            }

            Busy = true;
            string cut = ForgeTrackSession.NewWorkPath("reference-section");

            Kan.Forge.Pipeline.ForgeJobs.EditTrackAsync(reference.Path, Kan.Forge.Native.KanForgeEditMode.Trim, (float)fileFrom, (float)fileTo, cut, null, Scheduler)
               .ContinueWith(t => Schedule(() =>
               {
                   Busy = false;

                   if (t.IsFaulted)
                   {
                       ForgeNotify.Error(notifications, $"Could not cut the reference: {t.Exception?.GetBaseException().Message}");
                       return;
                   }

                   fillSection(cut, fileFrom + offset - ForgeTiming.TRANSCRIBER_DELAY_MS.Value, "the reference");
               }));
        }

        /// <summary>Transcribe <paramref name="audioPath"/> (it starts at <paramref name="startMs"/> on the level timeline) into the section.</summary>
        private void fillSection(string audioPath, double startMs, string what)
        {
            Busy = true;
            double from = SectionFromMs, to = SectionToMs;
            string instrument = ForgeTrackSession.Instrument;

            Kan.Forge.Pipeline.ForgeJobs.TranscribeAsync(audioPath, instrument, null, Scheduler)
               .ContinueWith(t => Schedule(() =>
               {
                   Busy = false;

                   if (t.IsFaulted)
                   {
                       ForgeNotify.Error(notifications, $"Transcription failed: {t.Exception?.GetBaseException().Message}");
                       return;
                   }

                   var notes = KanScore.FromDraft(t.Result, instrument, startMs).Notes;
                   int before = ScoreSession.Score.Notes.Count(n => n.StartMs >= from && n.StartMs < to);

                   record(null);
                   ScoreSession.Score.ReplaceSection(from, to, notes);
                   int after = ScoreSession.Score.Notes.Count(n => n.StartMs >= from && n.StartMs < to);

                   roll.Selected.Clear();
                   foreach (var n in ScoreSession.Score.Notes.Where(n => n.StartMs >= from && n.StartMs < to))
                       roll.Selected.Add(n);

                   roll.Reveal(from);
                   roll.Invalidate();
                   changed();

                   ForgeNotify.Info(notifications,
                       $"The section now has {ForgeFormat.Plural(after, "note", "notes")} from {what} (it had {before}). They are selected: adjust or move them; Ctrl+Z brings the old ones back.",
                       FontAwesome.Solid.Microphone);
               }));
        }

        // ------------------------------------------------------------------
        // Keys
        // ------------------------------------------------------------------

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            if (!IsPresent) return false;

            switch (e.Action)
            {
                case PlatformAction.Undo:
                    undo();
                    return true;

                case PlatformAction.Redo:
                    redo();
                    return true;

                case PlatformAction.Save:
                    saveToLevel(true);
                    return false; // the editor saves the level too

                case PlatformAction.SelectAll:
                    roll.Selected.Clear();
                    foreach (var n in ScoreSession.Score.Notes) roll.Selected.Add(n);
                    roll.Invalidate();
                    return true;

                case PlatformAction.Delete:
                    deleteSelection();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!IsPresent) return false;

            bool arrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
            if (e.Repeat && !arrow && e.Key is not (Key.PageUp or Key.PageDown or Key.Plus or Key.Minus or Key.KeypadPlus or Key.KeypadMinus)) return false;

            if (e.AltPressed) return false;

            var selected = roll.Selected.ToList();

            switch (e.Key)
            {
                case Key.Delete:
                case Key.BackSpace:
                    deleteSelection();
                    return true;

                case Key.Up:
                case Key.Down:
                    if (selected.Count == 0 || e.ControlPressed) return false; // the editor's own seeks

                    record("nudge-pitch");
                    ScoreSession.Score.Move(selected, 0, (e.Key == Key.Up ? 1 : -1) * (e.ShiftPressed ? 12 : 1));
                    break;

                case Key.Left:
                case Key.Right:
                {
                    if (e.ControlPressed && !e.ShiftPressed)
                    {
                        seekToNote(e.Key == Key.Right);
                        return true;
                    }

                    if (selected.Count == 0 || e.ControlPressed) return false; // the editor seeks one grid step

                    double step = e.ShiftPressed
                        ? 10
                        : EditorBeatmap.ControlPointInfo.TimingPointAt(selected.Min(n => n.StartMs)).BeatLength / Math.Max(1, beatDivisor.Value);

                    record("nudge-time");
                    ScoreSession.Score.Move(selected, e.Key == Key.Right ? step : -step, 0);
                    break;
                }

                default:
                    return !e.ControlPressed ? onPlainKey(e) : onControlKey(e);
            }

            ScoreSession.Score.Sort();
            roll.Invalidate();
            changed();
            return true;
        }

        /// <summary>Single-key shortcuts (listed in <see cref="ShortcutsPanel"/>).</summary>
        private bool onPlainKey(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case Key.S:
                case Key.Number1 when !e.ShiftPressed:
                    setTool(RollTool.Select);
                    return true;

                case Key.D:
                case Key.Number2 when !e.ShiftPressed:
                    setTool(RollTool.Draw);
                    return true;

                case Key.N:
                    toggleSnap();
                    return true;

                case Key.G:
                    if (e.ShiftPressed) gridTempo.NextFamily();
                    else gridTempo.NextDivision();
                    return true;

                case Key.T:
                    gridTempo.Tap();
                    return true;

                case Key.P:
                    monitor.NotesOn.Toggle();
                    return true;

                case Key.M:
                    monitor.CompanionOn.Toggle();
                    return true;

                case Key.R:
                    if (monitor.Reference != null) monitor.ReferenceOn.Toggle();
                    return true;

                case Key.F:
                    roll.Fit();
                    return true;

                case Key.Plus:
                case Key.KeypadPlus:
                case Key.Minus:
                case Key.KeypadMinus:
                    roll.ZoomTime(e.Key is Key.Plus or Key.KeypadPlus ? 0.5 : 2, clock.IsRunning || isOnScreen(clock.CurrentTime) ? clock.CurrentTime : roll.ViewStartMs + roll.ViewLengthMs / 2);
                    return true;

                case Key.K:
                    shortcuts.ToggleVisibility();
                    return true;

                case Key.U:
                    if (e.ShiftPressed)
                        removeRepeats();
                    else
                        selectPitchUnknown();
                    return true;

                case Key.Escape:
                    if (shortcuts.State.Value == Visibility.Visible)
                    {
                        shortcuts.Hide();
                        return true;
                    }

                    if (roll.Selected.Count == 0) return false; // the editor's own Esc
                    roll.Selected.Clear();
                    roll.Invalidate();
                    return true;

                case Key.Home:
                    clock.SeekSmoothlyTo(0);
                    return true;

                case Key.End:
                    clock.SeekSmoothlyTo(clock.TrackLength);
                    return true;

                case Key.PageUp:
                case Key.PageDown:
                    seekBar(e.Key == Key.PageDown);
                    return true;

                case Key.BracketLeft:
                    SectionStartHere();
                    return true;

                case Key.BracketRight:
                    SectionEndHere();
                    return true;

                case Key.L:
                    SectionFromSelection();
                    return true;
            }

            return false;
        }

        private bool onControlKey(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case Key.R when !e.ShiftPressed:
                    ToggleRecording();
                    return true;
            }

            return false;
        }

        private bool isOnScreen(double ms) => ms >= roll.ViewStartMs && ms <= roll.ViewStartMs + roll.ViewLengthMs;

        /// <summary>Back or forward one bar (Page Up / Page Down).</summary>
        private void seekBar(bool forward)
        {
            double now = clock.CurrentTimeAccurate;
            var tp = EditorBeatmap.ControlPointInfo.TimingPointAt(now);
            double bar = tp.BeatLength * tp.TimeSignature.Numerator;

            // Land on bar lines: from the timing point's first beat, in whole bars.
            double bars = (now - tp.Time) / bar;
            double target = forward ? Math.Floor(bars + 1e-6) + 1 : Math.Ceiling(bars - 1e-6) - 1;
            clock.SeekSmoothlyTo(Math.Clamp(tp.Time + target * bar, 0, clock.TrackLength));
        }

        /// <summary>The previous or next note's start (Ctrl + arrows).</summary>
        private void seekToNote(bool forward)
        {
            double now = clock.CurrentTimeAccurate;
            var notes = ScoreSession.Score.Notes;

            ScoreNote? target = forward
                ? notes.Where(n => n.StartMs > now + 1).MinBy(n => n.StartMs)
                : notes.Where(n => n.StartMs < now - 1).MaxBy(n => n.StartMs);

            if (target != null)
                clock.SeekSmoothlyTo(target.StartMs);
        }

        /// <summary>Select every note whose pitch the transcriber could not tell (U), to fix or delete them.</summary>
        private void selectPitchUnknown()
        {
            roll.Selected.Clear();
            foreach (var n in ScoreSession.Score.Notes.Where(n => n.PitchUnknown))
                roll.Selected.Add(n);
            roll.Invalidate();
            inspector.Invalidate();

            if (roll.Selected.Count == 0)
                ForgeNotify.Info(notifications, "Every note has a known pitch.", FontAwesome.Solid.Question);
        }

        /// <summary>One undoable step: the notes the transcriber heard twice go (see <see cref="KanScore.FindRepeats"/>).</summary>
        private void removeRepeats()
        {
            var repeats = ScoreSession.Score.FindRepeats(KanScore.IsMonophonic(ForgeTrackSession.Instrument)).ToHashSet();

            if (repeats.Count == 0)
            {
                ForgeNotify.Info(notifications, "No repeated notes: no two copies of a note start at the same moment.", FontAwesome.Solid.Clone);
                return;
            }

            record(null);
            ScoreSession.Score.Notes.RemoveAll(repeats.Contains);
            roll.Selected.Clear();
            roll.Invalidate();
            inspector.Invalidate();
            changed();

            ForgeNotify.Info(notifications, $"Removed {ForgeFormat.Plural(repeats.Count, "repeated note", "repeated notes")}. Ctrl+Z brings them back.", FontAwesome.Solid.Clone);
        }

        private void deleteSelection()
        {
            if (roll.Selected.Count == 0) return;

            record(null);
            ScoreSession.Score.Notes.RemoveAll(n => roll.Selected.Contains(n));
            roll.Selected.Clear();
            roll.Invalidate();
            changed();
        }
    }
}
