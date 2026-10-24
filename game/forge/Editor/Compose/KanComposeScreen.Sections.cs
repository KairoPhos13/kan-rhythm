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
    /// Section tools: fill a section from a recording or the reference's transcription, and export
    /// the score for validation.
    /// </summary>
    public partial class KanComposeScreen : EditorScreen, IKeyBindingHandler<PlatformAction>, SectionPanel.ITools
    {
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

            Kan.Forge.Pipeline.ForgeJobs.EditTrackAsync(reference.Path, Kan.Core.Native.KanForgeEditMode.Trim, (float)fileFrom, (float)fileTo, cut, null, Scheduler)
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

        // Validation export (owner 2026-10-14)
        // ------------------------------------------------------------------

        // Section tools: record with the mic, re-transcribe from the reference
        // ------------------------------------------------------------------
    }
}
