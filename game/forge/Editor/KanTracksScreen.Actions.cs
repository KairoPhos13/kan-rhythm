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
using Kan.Core.Native;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Core.Transcription;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The track tab's optional actions (none requires another): separate the instruments of a
    /// track, transcribe one, render the level audio; each a background job reported on its card.
    /// </summary>
    public partial class KanTracksScreen : EditorScreen, IKeyBindingHandler<PlatformAction>
    {
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
                    ? "Nothing to separate yet: import a track in Resources, then give it the demux chip."
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
                    string done = $"Demux done: {ForgeFormat.Plural(stemsAdded, "stem", "stems")} joined the set.";
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
                transcribeCard.Hint("Give one track the reference chip; transcription uses it automatically.");
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

                    string done = $"Transcription ready from '{reference.Name}': {describe(draft)}, shown on its track and offered in the compose tab.";
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
                    renderCard.Hint("Mark at least one audible track as companion; the level audio is their mix.");
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

        // ------------------------------------------------------------------

    }
}
