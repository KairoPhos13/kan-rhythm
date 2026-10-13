// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Edit;
using Kan.Forge.Native;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's **tracks** tab (owner 2026-10-13: the track editor lives
    /// next to setup/compose/design/timing/verify). It shows the unified
    /// resource list — roles (reference / accompaniment / demux), volume,
    /// mute, per-track waveform previews — plus the non-destructive track
    /// editor and the optional actions. Everything here is **selection and
    /// arrangement**: importing happens only in Resources
    /// (<see cref="KanMediaSection"/>).
    /// </summary>
    public partial class KanTracksScreen : EditorScreen
    {
        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private readonly ForgeTrackSet trackSet = ForgeTrackSession.Tracks;

        private FillFlowContainer trackFlow = null!;
        private FormButton demuxButton = null!;
        private FormButton transcribeButton = null!;
        private FormButton renderButton = null!;
        private KanProgressBar demuxProgress = null!;
        private KanProgressBar transcribeProgress = null!;
        private OsuSpriteText statusText = null!;

        public KanTracksScreen()
            : base(EditorScreenMode.Tracks)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            demuxButton = new FormButton
            {
                Caption = @"Demux selected tracks (optional)",
                ButtonText = @"run",
                Action = startDemux,
            };
            demuxProgress = new KanProgressBar();
            transcribeButton = new FormButton
            {
                Caption = @"Transcribe the reference track (optional)",
                ButtonText = @"run",
                Action = startTranscription,
            };
            transcribeProgress = new KanProgressBar();
            renderButton = new FormButton
            {
                Caption = @"Render single track (mixdown of the accompaniments)",
                ButtonText = @"render",
                Action = startRender,
            };
            statusText = new OsuSpriteText
            {
                Font = OsuFont.GetFont(size: 12),
                Text = @"Ready.",
            };

            trackFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 8),
            };

            Add(new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 12),
                    Padding = new MarginPadding(20),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                            Text = @"Tracks (imported & created)",
                        },
                        new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 10),
                            Alpha = 0.8f,
                            Text = @"importing happens in Resources (setup) — everything here selects and arranges",
                        },
                        trackFlow,
                        new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                            Text = @"Actions (selection only — nothing is mandatory)",
                        },
                        demuxButton,
                        demuxProgress,
                        transcribeButton,
                        transcribeProgress,
                        renderButton,
                        statusText,
                    },
                },
            });

            rebuildTrackRows();
        }

        // ------------------------------------------------------------------
        // Track list
        // ------------------------------------------------------------------

        private void removeTrack(ForgeTrack track)
        {
            trackSet.Remove(track);
            rebuildTrackRows();
        }

        private void onRoleChanged()
        {
            // Deferred: role toggles fire from inside a row's own controls,
            // and rebuilding disposes that row.
            Scheduler.Add(rebuildTrackRows);
        }

        private void rebuildTrackRows()
        {
            trackFlow.Clear();

            if (trackSet.Tracks.Count == 0)
            {
                trackFlow.Add(new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 11),
                    Alpha = 0.7f,
                    Text = @"no tracks yet — import audio in Resources (setup); the video's audio joins automatically",
                });
                return;
            }

            foreach (var track in trackSet.Tracks)
                trackFlow.Add(new TrackRow(trackSet, track, removeTrack, onRoleChanged, createEditedTrack, connectMusic));
        }

        /// <summary>
        /// The "music" role connects the track to the editor's Audio Track
        /// (the stock row in Resources): the selected track becomes the
        /// beatmap's audio — the music the level is created around (owner
        /// 2026-10-13: no second import surface; the row reflects the role).
        /// </summary>
        private void connectMusic(ForgeTrack track)
        {
            string? applied = applyAsBeatmapAudio(track.Path);
            status(applied != null
                ? $"'{track.Name}' is now the Audio Track (the level's music)."
                : $"Could not connect '{track.Name}' to the Audio Track.");
        }

        /// <summary>Refresh the list (called by the import surface).</summary>
        public void RefreshTracks() => Scheduler.Add(rebuildTrackRows);

        protected override void PopIn()
        {
            base.PopIn();
            // Imports land in the shared session while this screen is away;
            // refresh whenever the user comes back to the tab.
            rebuildTrackRows();
        }

        /// <summary>Analyse a track off-thread (waveform + meaningful-audio marking).</summary>
        private void analyzeLater(ForgeTrack track)
            => Task.Run(() => TrackAnalysis.Analyze(track))
                   .ContinueWith(_ => Scheduler.Add(rebuildTrackRows));

        // ------------------------------------------------------------------
        // Track editor (non-destructive: edits create NEW named tracks)
        // ------------------------------------------------------------------

        private void createEditedTrack(ForgeTrack source, string name, string fromSeconds,
                                       string toSeconds, KanForgeEditMode mode)
        {
            if (!float.TryParse(fromSeconds, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float from)
                || !float.TryParse(toSeconds, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float to)
                || from < 0 || to <= from)
            {
                status("Edit: give a valid range (0 <= from < to, seconds).");
                return;
            }

            string trackName = string.IsNullOrWhiteSpace(name) ? $"{source.Name} (edit)" : name.Trim();

            setBusy(true);
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "edit-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            runJob(() => ForgeJobs.EditTrackAsync(source.Path, mode, from * 1000, to * 1000, outWav, null, Scheduler),
                transcribeProgress,
                path =>
                {
                    var created = trackSet.Import(trackName, path);
                    if (created == null)
                    {
                        status($"'{trackName}' is already in the set.");
                        return;
                    }

                    created.IsSeparated = source.IsSeparated;
                    created.IsMixdown = source.IsMixdown;
                    rebuildTrackRows();
                    analyzeLater(created);
                    status($"New track '{trackName}' added ({trackSet.Tracks.Count} in the set).");
                },
                ex => status($"Edit failed: {ex.Message}"));
        }

        // ------------------------------------------------------------------
        // Optional actions — none of them requires another
        // ------------------------------------------------------------------

        private void startDemux()
        {
            var queue = trackSet.DemuxQueue.ToList();
            if (queue.Count == 0)
            {
                status(trackSet.Tracks.Count == 0
                    ? "Demux is optional — import a track (Resources) and mark it 'demux' first."
                    : "Demux is optional — mark at least one eligible track 'demux' first.");
                return;
            }

            setBusy(true);
            demuxProgress.Set(0, "starting");

            runJob(() => Task.Run(() => demuxAll(queue)),
                demuxProgress,
                stemsAdded =>
                {
                    // Meaningful stems first; "no meaningful audio" stems go
                    // to the end of the list so they don't mislead.
                    trackSet.OrderByMeaning();
                    rebuildTrackRows();
                    status($"Demux done — {stemsAdded} stem tracks joined the list.");
                },
                ex => status($"Demux failed: {ex.Message}"));
        }

        private int demuxAll(List<ForgeTrack> queue)
        {
            int stemsAdded = 0;

            foreach (var track in queue)
            {
                string outDir = Path.Combine(Path.GetTempPath(), "kan-forge",
                                             "stems-" + Guid.NewGuid().ToString("N")[..8]);

                Scheduler.Add(() => demuxProgress.Set(0, $"demux {track.Name}"));
                ForgeJobs.SeparateStemsAsync(track.Path, outDir).GetAwaiter().GetResult();

                // The source has been demuxed (never again), and the results
                // join the list as separated tracks (any role except demux).
                track.DemuxPending = false;
                track.IsSeparated = true;

                if (!Directory.Exists(outDir)) continue;

                foreach (string file in Directory.EnumerateFiles(Path.Combine(outDir, "roformer"), "*.wav")
                                                 .OrderBy(f => f))
                {
                    var stem = trackSet.Import(Path.GetFileNameWithoutExtension(file), file);
                    if (stem == null) continue;

                    stem.IsSeparated = true;

                    // Label stems that seem to carry no meaningful audio
                    // (waveform + marking) — on this worker thread already.
                    TrackAnalysis.Analyze(stem);
                    stemsAdded++;
                }
            }

            return stemsAdded;
        }

        private void startTranscription()
        {
            var reference = trackSet.Reference;
            if (reference == null)
            {
                status("Transcription is optional — mark one track as 'reference' first.");
                return;
            }

            setBusy(true);
            transcribeProgress.Set(0, "starting");

            runJob(() => ForgeJobs.TranscribeAsync(reference.Path, @"keyboard", report(transcribeProgress), Scheduler),
                transcribeProgress,
                draft => status($"Transcription ready from '{reference.Name}': {describe(draft)}"),
                ex => status($"Transcription failed: {ex.Message}"));
        }

        private void startRender()
        {
            var accomp = trackSet.Accompaniments.ToList();
            if (accomp.Count == 0)
            {
                status("Render: select at least one 'accompaniment' track first.");
                return;
            }

            setBusy(true);
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "render-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            runJob(() => ForgeJobs.RenderMixAsync(accomp, outWav, null, Scheduler),
                transcribeProgress,
                path =>
                {
                    string? applied = applyAsBeatmapAudio(path);
                    status(applied != null
                        ? $"Rendered {accomp.Count} accompaniments → {applied} (now the beatmap audio)."
                        : $"Rendered {accomp.Count} accompaniments → {path} (could not set it as beatmap audio).");
                },
                ex => status($"Render failed: {ex.Message}"));
        }

        private string? applyAsBeatmapAudio(string sourcePath)
        {
            try
            {
                var set = working.Value.BeatmapSetInfo;

                string name = @"audio-render.wav";
                if (!ForgeTrack.LooksLikeMixdown(sourcePath))
                    name = Path.GetFileName(sourcePath);

                for (int i = 1; set.Files.Any(f => f.Filename == name); i++)
                    name = Path.GetFileNameWithoutExtension(name) + $"-{i}" + Path.GetExtension(name);

                using (var stream = File.OpenRead(sourcePath))
                    beatmaps.AddFile(set, stream, name);

                working.Value.BeatmapInfo.Metadata.AudioFile = name;
                return name;
            }
            catch (Exception e)
            {
                status($"Could not set the beatmap audio: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        private async void runJob<T>(Func<Task<T>> body, KanProgressBar progress,
                                     Action<T> onDone, Action<Exception> onFail)
        {
            try
            {
                T result = await body();
                progress.Complete();
                onDone(result);
            }
            catch (Exception ex)
            {
                progress.Fail();
                onFail(ex);
            }
            finally
            {
                setBusy(false);
            }
        }

        private static Action<ForgeProgress> report(KanProgressBar progress)
            => p => progress.Set(p.Percent, p.Phase);

        private void setBusy(bool busy)
        {
            demuxButton.Enabled.Value = !busy;
            transcribeButton.Enabled.Value = !busy;
            renderButton.Enabled.Value = !busy;
        }

        private void status(string message)
        {
            Scheduler.Add(() => statusText.Text = message);
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

        /// <summary>
        /// One row of the unified track list: waveform, roles (reference /
        /// accompaniment / demux), volume, mute, the track editor, remove.
        /// </summary>
        private partial class TrackRow : Container
        {
            public TrackRow(ForgeTrackSet set, ForgeTrack track, Action<ForgeTrack> onRemove,
                            Action onRoleChanged,
                            Action<ForgeTrack, string, string, string, KanForgeEditMode> onCreateEdited,
                            Action<ForgeTrack> onMusicSelected)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                CornerRadius = 6;
                Masking = true;

                var gain = new BindableNumber<float>(track.Gain)
                {
                    MinValue = 0,
                    MaxValue = 2,
                    Precision = 0.05f,
                };
                var muted = new BindableBool(track.Muted);
                var reference = new BindableBool(track.IsReference);
                var music = new BindableBool(track.IsMusic);
                var accomp = new BindableBool(track.IsAccompaniment);
                var demux = new BindableBool(track.DemuxPending);

                gain.BindValueChanged(v => track.Gain = v.NewValue);
                muted.BindValueChanged(v => track.Muted = v.NewValue);
                reference.BindValueChanged(v =>
                {
                    if (v.NewValue)
                        set.SetReference(track);
                    else
                        track.IsReference = false;

                    onRoleChanged();
                });
                music.BindValueChanged(v =>
                {
                    if (v.NewValue)
                    {
                        set.SetMusic(track);
                        onMusicSelected(track);
                    }
                    else
                        track.IsMusic = false;

                    onRoleChanged();
                });
                accomp.BindValueChanged(v => track.IsAccompaniment = v.NewValue);
                demux.BindValueChanged(v =>
                {
                    if (!track.CanDemux && v.NewValue)
                    {
                        demux.Value = false;
                        return;
                    }

                    track.DemuxPending = v.NewValue;
                });

                var nameBox = new FormTextBox
                {
                    Caption = @"new track name",
                    PlaceholderText = $@"{track.Name} (edit)",
                };
                var fromBox = new FormNumberBox(true)
                {
                    Caption = @"from (s)",
                };
                var toBox = new FormNumberBox(true)
                {
                    Caption = @"to (s)",
                };

                var content = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 4),
                    Padding = new MarginPadding(10),
                };

                content.Add(new OsuSpriteText
                {
                    Text = track.Name,
                    Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                });

                if (track.LikelyEmpty)
                {
                    content.Add(new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 10),
                        Colour = new Colour4(1.0f, 0.75f, 0.25f, 1),
                        Text = @"⚠ seems to contain no meaningful audio — select only if you are sure",
                    });
                }

                content.AddRange(new Drawable[]
                {
                    new FormCheckBox
                    {
                        Caption = track.CanDemux
                            ? @"demux (separate into stems)"
                            : @"demux (already separated — not again)",
                        Current = demux,
                    },
                    new FormCheckBox
                    {
                        Caption = @"reference (the take to transcribe)",
                        Current = reference,
                    },
                    new FormCheckBox
                    {
                        Caption = @"music (the level's base track)",
                        Current = music,
                    },
                    new FormCheckBox
                    {
                        Caption = @"accompaniment (mixed into the beatmap audio)",
                        Current = accomp,
                    },
                    new FormSliderBar<float>
                    {
                        Caption = @"volume",
                        Current = gain,
                        KeyboardStep = 0.05f,
                    },
                    new FormCheckBox
                    {
                        Caption = @"mute",
                        Current = muted,
                    },
                    new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                        Text = @"edit → new track (the source is never modified)",
                        Margin = new MarginPadding { Top = 6 },
                    },
                    nameBox,
                    fromBox,
                    toBox,
                    new FormButton
                    {
                        Caption = @"Keep range only",
                        ButtonText = @"create",
                        Action = () => onCreateEdited(track, nameBox.Current.Value ?? string.Empty,
                                                      fromBox.Current.Value ?? string.Empty,
                                                      toBox.Current.Value ?? string.Empty,
                                                      KanForgeEditMode.Keep),
                    },
                    new FormButton
                    {
                        Caption = @"Silence range",
                        ButtonText = @"create",
                        Action = () => onCreateEdited(track, nameBox.Current.Value ?? string.Empty,
                                                      fromBox.Current.Value ?? string.Empty,
                                                      toBox.Current.Value ?? string.Empty,
                                                      KanForgeEditMode.Mute),
                    },
                    new FormButton
                    {
                        Caption = @"Trim to range (offset lost)",
                        ButtonText = @"create",
                        Action = () => onCreateEdited(track, nameBox.Current.Value ?? string.Empty,
                                                      fromBox.Current.Value ?? string.Empty,
                                                      toBox.Current.Value ?? string.Empty,
                                                      KanForgeEditMode.Trim),
                    },
                    new FormButton
                    {
                        Caption = @"remove",
                        ButtonText = @"✕",
                        Action = () => onRemove(track),
                    },
                });

                Children = new Drawable[]
                {
                    // Selection highlight: role-bearing rows stand out.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = track.HasAnyRole
                            ? new Colour4(0.25f, 0.55f, 0.5f, 0.35f)
                            : new Colour4(0, 0, 0, 0.25f),
                    },
                    // The waveform as the row's visual background.
                    new WaveformPreview(track)
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.18f,
                    },
                    content,
                };
            }
        }

        /// <summary>Determinate progress bar with a phase caption.</summary>
        private partial class KanProgressBar : Container
        {
            private readonly Box fill;
            private readonly OsuSpriteText label;

            public KanProgressBar()
            {
                AutoSizeAxes = Axes.Y;
                RelativeSizeAxes = Axes.X;

                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 6,
                        CornerRadius = 3,
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Colour4(0, 0, 0, 0.4f),
                            },
                            fill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0,
                                Colour = new Colour4(1, 1, 1, 0.8f),
                            },
                        },
                    },
                    label = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 10),
                        Margin = new MarginPadding { Top = 8 },
                    },
                };
            }

            public void Set(int percent, string phase)
            {
                fill.Width = Math.Clamp(percent, 0, 100) / 100.0f;
                label.Text = $"{phase}… {percent}%";
            }

            public void Complete()
            {
                fill.Width = 1;
                label.Text = @"done";
            }

            public void Fail() => label.Text = @"failed";
        }
    }
}
