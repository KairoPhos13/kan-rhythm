// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Screens.Edit;
using Kan.Forge.Media;
using Kan.Core.Score;
using Kan.Forge.Tracks;
using Kan.Forge.Audio;
using Kan.Core.Tracks;
using Kan.Core.Media;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// What the compose tab plays (owner 2026-10-14: toggles for the
    /// companion and the reference, to check how well the notes sit). The
    /// editor clock runs on the level audio (the companion), which is
    /// silenced when its toggle is off; the reference plays in sync on its
    /// own channel, where it sits on the level audio's timeline. The notes
    /// toggle plays the score itself (<see cref="ScoreSynth"/>), rendered a
    /// moment after each edit, for composing by ear against the companion.
    /// </summary>
    internal partial class ComposeMonitor : Component
    {
        private const double max_drift_ms = 40;

        public readonly BindableBool Active = new BindableBool();

        public readonly BindableBool CompanionOn = new BindableBool(true);

        public readonly BindableBool ReferenceOn = new BindableBool();

        public readonly BindableBool NotesOn = new BindableBool();

        /// <summary>Wait this long after an edit before rendering the notes again (edits come in bursts).</summary>
        private const double notes_render_delay_ms = 400;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private EditorClock clock { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private readonly BindableDouble silence = new BindableDouble(0);
        private Track? silenced;

        private AudioChannel? reference;
        private ForgeTrack? referenceTrack;
        private string? referencePath;

        private AudioChannel? notes;
        private string? notesPath;
        private int notesVersion, renderedVersion = -1;
        private double renderAfter;
        private bool rendering;

        /// <summary>The reference track, if the set has one.</summary>
        public ForgeTrack? Reference => ForgeTrackSession.Tracks.Reference;

        /// <summary>Where the reference's file starts on the level audio's timeline.</summary>
        public static double ReferenceOffsetMs(ForgeTrack reference) => reference.OffsetMs - ForgeTrackSession.Tracks.AudioStartMs;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Active.BindValueChanged(v =>
            {
                if (!v.NewValue)
                {
                    reference?.Track.Stop();
                    notes?.Track.Stop();
                }

                updateCompanion();
            }, true);
            CompanionOn.BindValueChanged(_ => updateCompanion(), true);
        }

        protected override void Update()
        {
            base.Update();

            updateCompanion();
            syncReference();
            syncNotes();
        }

        /// <summary>The score changed: render its sound again shortly.</summary>
        public void NotesChanged()
        {
            notesVersion++;
            renderAfter = Time.Current + notes_render_delay_ms;
        }

        /// <summary>The raw level-audio position (the editor clock adds offsets; see SetMonitor).</summary>
        private double levelAudioTime()
        {
            var levelTrack = working.Value.TrackLoaded ? working.Value.Track : null;
            return levelTrack != null && ReferenceEquals(clock.Source, levelTrack) ? levelTrack.CurrentTime : clock.CurrentTime;
        }

        private void syncNotes()
        {
            if (Active.Value && NotesOn.Value && !rendering && renderedVersion != notesVersion && Time.Current >= renderAfter)
                renderNotes();

            if (notes == null) return;

            var t = notes.Track;
            if (!t.IsLoaded) return;

            bool running = Active.Value && NotesOn.Value && clock.IsRunning;
            notes.Sync.Update(t, levelAudioTime(), running, Time.Current);
            notes.Sync.TakeResyncs();
        }

        private void renderNotes()
        {
            rendering = true;
            int version = notesVersion;
            var snapshot = ScoreSession.Score.Notes.Select(n => n.Clone()).ToList();
            string path = ForgeTrackSession.NewWorkPath(@"score-notes");

            Task.Run(() =>
            {
                float[] samples = ScoreSynth.Render(snapshot);
                WavFile.Write(path, samples, samples.Length, 1, ScoreSynth.SAMPLE_RATE);
            }).ContinueWith(task => Schedule(() =>
            {
                rendering = false;
                renderedVersion = version;

                if (task.IsFaulted)
                {
                    osu.Framework.Logging.Logger.Log($"kan!forge: could not render the notes: {task.Exception?.GetBaseException().Message}", level: osu.Framework.Logging.LogLevel.Important);
                    return;
                }

                closeNotes();
                notesPath = path;
                notes = AudioChannel.Open(audio, new ForgeTrack(@"score notes", path));
                notes?.Track.AddAdjustment(AdjustableProperty.Tempo, clock.AudioAdjustments.AggregateTempo);
                notes?.Track.AddAdjustment(AdjustableProperty.Frequency, clock.AudioAdjustments.AggregateFrequency);
            }));
        }

        private void closeNotes()
        {
            notes?.Dispose();
            notes = null;

            if (notesPath == null) return;

            try
            {
                System.IO.File.Delete(notesPath);
            }
            catch
            {
                // A leftover preview in the work folder does no harm.
            }

            notesPath = null;
        }

        private void updateCompanion()
        {
            Track? target = Active.Value && !CompanionOn.Value && working.Value.TrackLoaded ? working.Value.Track : null;
            if (ReferenceEquals(target, silenced)) return;

            silenced?.RemoveAdjustment(AdjustableProperty.Volume, silence);
            silenced = target;
            silenced?.AddAdjustment(AdjustableProperty.Volume, silence);
        }

        private void syncReference()
        {
            var track = Reference;

            // A timeline repair gives the same track a new file.
            if (!ReferenceEquals(track, referenceTrack) || track?.Path != referencePath)
            {
                reference?.Dispose();
                reference = null;
                referenceTrack = track;
                referencePath = track?.Path;

                if (track != null)
                {
                    reference = AudioChannel.Open(audio, track);
                    reference?.Track.AddAdjustment(AdjustableProperty.Tempo, clock.AudioAdjustments.AggregateTempo);
                    reference?.Track.AddAdjustment(AdjustableProperty.Frequency, clock.AudioAdjustments.AggregateFrequency);
                }
            }

            if (reference == null || track == null) return;

            var t = reference.Track;
            if (!t.IsLoaded) return;

            bool running = Active.Value && ReferenceOn.Value && clock.IsRunning;

            double filePos = levelAudioTime() - ReferenceOffsetMs(track);
            bool inside = filePos >= 0 && filePos < t.Length;

            double heardAt = t.IsRunning ? t.CurrentTime : filePos;
            // The track tab's mute/solo don't apply here: in
            // compose the reference toggle alone decides whether it is heard.
            bool heard = track.InClip(heardAt) && track.Edits.IsAudibleAt(heardAt);
            t.Volume.Value = heard ? Math.Clamp(track.Gain, 0, 1) : 0;
            reference.ApplyBoost(track.Gain);
            reference.ApplyFilters(track.Edits);

            reference.Sync.Update(t, filePos, running && inside, Time.Current);

            int resyncs = reference.Sync.TakeResyncs();
            if (resyncs > 0)
                osu.Framework.Logging.Logger.Log($"kan!forge: reference resynced {resyncs}× (audible)", level: osu.Framework.Logging.LogLevel.Verbose);
        }

        protected override void Dispose(bool isDisposing)
        {
            silenced?.RemoveAdjustment(AdjustableProperty.Volume, silence);
            silenced = null;
            reference?.Dispose();
            reference = null;
            closeNotes();

            base.Dispose(isDisposing);
        }
    }
}
