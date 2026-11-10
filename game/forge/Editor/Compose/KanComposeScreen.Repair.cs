// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Overlays;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Repairing the audio's timing from compose (owner 2026-10-22: "the
    /// companion changes tempo because it feels like it, the solo rushes it;
    /// it sounds fine until I want to learn it"). The audio is brought to the
    /// grid: silence put in at the playhead, the section cut out, or the
    /// section played to a new length. Every track of the set takes the same
    /// edit (the reference too: the level preview and autoplay hear it
    /// repaired), and so do the notes, the design and the tempo changes after
    /// it, which mark where the music is (<see cref="TimelineRepair"/>).
    /// </summary>
    public partial class KanComposeScreen
    {
        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private osu.Game.Screens.Edit.Editor? editor { get; set; }

        /// <summary>The length the repairs put in, in beats at the playhead (1, ½, ¼, ⅛, 1/16); NaN = <see cref="RepairLengthMs"/>.</summary>
        public double RepairBeats { get; set; } = 1;

        /// <summary>The length the repairs put in when typed (ms; owner 2026-10-22: "custom, exactly how much").</summary>
        public double RepairLengthMs { get; set; } = double.NaN;

        /// <summary>The length a repair puts in (ms).</summary>
        public double RepairLength => !double.IsNaN(RepairBeats) ? RepairBeats * beatAt(clock.CurrentTime)
            : !double.IsNaN(RepairLengthMs) ? RepairLengthMs : beatAt(clock.CurrentTime);

        /// <summary>
        /// What puts the next tempo change back in step with the tempo at the
        /// playhead (owner 2026-10-22: "the metronome is already fitted in the
        /// timing tab: it could tell how much syncs this tempo with the next
        /// segment"): the music from the next change on came in off this
        /// tempo's beats by a fraction of one. Silence put in (it came early),
        /// or that much cut (late), the smaller, at the playhead, where the
        /// creator says the time fits; everything after moves with the music.
        /// Null when there is no next change, or it is already on a beat.
        /// </summary>
        public (double TempoMs, double DeltaMs, double AtMs)? NextTempoSync => nextTempoSync() is { } s ? (s.TempoMs, s.Edit.DeltaMs, s.Edit.FromMs) : null;

        /// <summary>The next tempo change put back on the beats of the one at the playhead, by an edit at the playhead (where the creator says the time fits).</summary>
        private (double TempoMs, TimelineEdit Edit)? nextTempoSync()
        {
            var points = EditorBeatmap.ControlPointInfo.TimingPoints;
            if (points.Count < 2) return null;

            double now = clock.CurrentTime;
            int i = 0;
            while (i + 1 < points.Count && points[i + 1].Time <= now + 0.5) i++;
            if (i + 1 >= points.Count) return null;

            return TimelineEdit.SyncNext(points[i].Time, points[i].BeatLength, points[i + 1].Time, now) is { } edit ? (points[i + 1].Time, edit) : null;
        }

        public void SyncWithNextTempo()
        {
            if (nextTempoSync() is not { } sync) return;

            var edit = sync.Edit;

            repair(edit, edit.DeltaMs > 0
                ? $"Put in {edit.DeltaMs:0} ms of silence at {ForgeFormat.Time(edit.FromMs / 1000)}: the tempo change at {ForgeFormat.Time(sync.TempoMs / 1000)} is in step with the tempo before"
                : $"Cut {-edit.DeltaMs:0} ms at {ForgeFormat.Time(edit.FromMs / 1000)}: the tempo change at {ForgeFormat.Time(sync.TempoMs / 1000)} is in step with the tempo before");
        }

        // Before the first tempo, its beat (not osu!'s default).
        private double beatAt(double ms)
        {
            var points = EditorBeatmap.ControlPointInfo.TimingPoints;
            return (points.LastOrDefault(p => p.Time <= ms) ?? points.FirstOrDefault())?.BeatLength ?? 500;
        }

        /// <summary>One step of the grid (a beat over the divisor) at <paramref name="ms"/>.</summary>
        private double gridStepAt(double ms) => beatAt(ms) / Math.Max(1, beatDivisor.Value);

        public void InsertSilenceHere() => repair(TimelineEdit.InsertSilence(Math.Max(0, clock.CurrentTime), RepairLength),
            $"Put in {RepairLength:0} ms of silence at {ForgeFormat.Time(clock.CurrentTime / 1000)}");

        public void CutSection()
        {
            if (!HasSection) return;

            repair(TimelineEdit.Cut(SectionFromMs, SectionToMs), $"Cut out {SectionToMs - SectionFromMs:0} ms");
        }

        public void StretchSection()
        {
            if (!HasSection) return;

            repair(TimelineEdit.Stretch(SectionFromMs, SectionToMs, RepairLength),
                $"Played the section ({SectionToMs - SectionFromMs:0} ms) in {RepairLength:0} ms");
        }

        public void FitSectionToGrid()
        {
            if (!HasSection) return;

            double length = SectionToMs - SectionFromMs;
            double step = gridStepAt(SectionFromMs);
            double fitted = Math.Max(1, Math.Round(length / step)) * step;

            if (Math.Abs(fitted - length) < 1)
            {
                ForgeNotify.Info(notifications, "The section already spans a whole number of grid steps.", FontAwesome.Solid.Ruler);
                return;
            }

            repair(TimelineEdit.Stretch(SectionFromMs, SectionToMs, fitted),
                $"Played the section ({length:0} ms) in {fitted:0} ms ({fitted / step:0} grid steps)");
        }

        private void repair(TimelineEdit edit, string done)
        {
            if (Busy || Recording || edit.IsEmpty) return;

            var set = ForgeTrackSession.Tracks;

            if (set.Tracks.Count == 0)
            {
                ForgeNotify.Info(notifications, "There are no tracks to repair: add the level's audio in the track tab.", FontAwesome.Solid.Music);
                return;
            }

            var setEdit = edit.Shifted(set.AudioStartMs);
            var jobs = set.Tracks.Select(t => (t, ForgeTrackSession.NewWorkPath(@"repaired"))).ToList();

            Busy = true;
            clock.Stop();

            Task.Run(() => TimelineRepair.Render(setEdit, jobs)).ContinueWith(t => Schedule(() =>
            {
                Busy = false;

                if (t.IsFaulted)
                {
                    ForgeNotify.Error(notifications, $"Could not repair the audio: {t.Exception?.GetBaseException().Message}");
                    return;
                }

                int removed = TimelineRepair.Apply(edit, t.Result, grid ??= new EditorGrid(this));

                // The section now spans what it became (a cut leaves none).
                if (edit.Kind == TimelineEditKind.Cut)
                    SectionFromMs = SectionToMs = double.NaN;
                else if (HasSection)
                {
                    SectionFromMs = edit.Map(SectionFromMs);
                    SectionToMs = edit.Map(SectionToMs);
                }

                roll.Selected.Clear();
                roll.Invalidate();
                changed();

                int tracks = t.Result.Count(r => r.NewPath != null);
                ForgeNotify.Info(notifications,
                    $"{done}: {ForgeFormat.Plural(tracks, "track", "tracks")} repaired, the notes moved with them"
                    + (removed > 0 ? $" ({ForgeFormat.Plural(removed, "note", "notes")} cut out)" : string.Empty)
                    + ". The level audio is being updated; Ctrl+Z undoes it all.",
                    FontAwesome.Solid.Cut);

                renderLevelAudio();
            }));
        }

        /// <summary>
        /// Developer tooling (KAN_DEV_REPAIR): "insert:at:length",
        /// "cut:from-to", "stretch:from-to:length", "undo", "redo" (level ms);
        /// "copy:from-to" and "paste:at" (the notes starting in the span), "range:low-high" (MIDI).
        /// </summary>
        public void DevRepair(string step)
        {
            string[] p = step.Split(':');
            double num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            (double, double) span(string s) => (num(s.Split('-')[0]), num(s.Split('-')[1]));

            logState($"before {step}");

            switch (p[0])
            {
                case @"insert":
                    clock.Seek(num(p[1]));
                    RepairBeats = double.NaN;
                    RepairLengthMs = num(p[2]);
                    InsertSilenceHere();
                    break;

                case @"cut":
                    (SectionFromMs, SectionToMs) = span(p[1]);
                    CutSection();
                    break;

                case @"stretch":
                    (SectionFromMs, SectionToMs) = span(p[1]);
                    RepairBeats = double.NaN;
                    RepairLengthMs = num(p[2]);
                    StretchSection();
                    break;

                case @"change":
                    clock.Seek(num(p[1]));
                    Scheduler.AddDelayed(() => this.ChildrenOfType<GridTempoBar>().First().addTempoChangeHere(), 200);
                    break;

                case @"beat":
                    clock.Seek(num(p[1]));
                    Scheduler.AddDelayed(() => this.ChildrenOfType<GridTempoBar>().First().beatHere(), 200);
                    break;

                case @"scale":
                    this.ChildrenOfType<GridTempoBar>().First().ScaleBeat(num(p[1]));
                    break;

                case @"key":
                    bool sounded = audition.Play((int)num(p[1]));
                    osu.Framework.Logging.Logger.Log($"kan! dev repair: key {p[1]} {(sounded ? "sounded" : "had no sample")}", osu.Framework.Logging.LoggingTarget.Runtime, osu.Framework.Logging.LogLevel.Important);
                    break;

                case @"firstbeat":
                    this.ChildrenOfType<GridTempoBar>().First().moveFirstBeat(num(p[1]));
                    break;

                case @"sync":
                    clock.Seek(num(p[1]));
                    Scheduler.AddDelayed(() =>
                    {
                        osu.Framework.Logging.Logger.Log($"kan! dev repair: next tempo sync {NextTempoSync}", osu.Framework.Logging.LoggingTarget.Runtime, osu.Framework.Logging.LogLevel.Important);
                        SyncWithNextTempo();
                    }, 200);
                    break;

                case @"copy":
                {
                    var (from, to) = span(p[1]);
                    roll.Selected.Clear();
                    foreach (var n in ScoreSession.Score.Notes.Where(n => n.StartMs >= from && n.StartMs < to)) roll.Selected.Add(n);
                    Copy();
                    roll.Invalidate();
                    break;
                }

                case @"paste":
                    clock.Seek(num(p[1]));
                    Scheduler.AddDelayed(() =>
                    {
                        Paste();
                        osu.Framework.Logging.Logger.Log($"kan! dev repair: pasted {roll.Selected.Count} notes from {roll.Selected.Min(n => n.StartMs):0} ms, {ScoreSession.Score.Notes.Count} in all",
                            osu.Framework.Logging.LoggingTarget.Runtime, osu.Framework.Logging.LogLevel.Important);
                    }, 200);
                    break;

                case @"range":
                {
                    var (low, high) = span(p[1]);
                    Kan.Core.Play.RangeFit.PlayerRange = () => ((int)low, (int)high);
                    break;
                }

                case @"show":
                {
                    // The repair controls in view, for a screenshot.
                    var panel = this.ChildrenOfType<SectionPanel>().First();
                    panel.FindClosestParent<osu.Game.Graphics.Containers.OsuScrollContainer>()?.ScrollTo(panel.DrawHeight * 0.45f);
                    clock.Seek(num(p[1]));
                    break;
                }

                case @"undo":
                    undo();
                    break;

                case @"redo":
                    redo();
                    break;
            }

            Scheduler.AddDelayed(() => logState($"after {step}"), 10000);
        }

        private void logState(string when)
        {
            var notes = ScoreSession.Score.Notes;
            string tracks = string.Join(", ", ForgeTrackSession.Tracks.Tracks.Select(t => $"{t.Name} @{t.OffsetMs:0} {System.IO.Path.GetFileName(t.Path)}"));
            var points = EditorBeatmap.ControlPointInfo.TimingPoints;
            osu.Framework.Logging.Logger.Log($"kan! dev repair {when}: {notes.Count} notes, first {(notes.Count > 0 ? notes[0].StartMs : 0):0.#} last {(notes.Count > 0 ? notes[^1].StartMs : 0):0.#}; "
                                             + $"grid {points.Count} points ({string.Join(" ", points.Take(5).Select(p => p.Time.ToString("0.#")))}…), at the playhead {beatAt(clock.CurrentTime):0.###} ms/beat; "
                                             + $"level audio {working.Value.Metadata.AudioFile} ({(working.Value.TrackLoaded ? working.Value.Track.Length : 0):0} ms); tracks: {tracks}",
                osu.Framework.Logging.LoggingTarget.Runtime, osu.Framework.Logging.LogLevel.Important);
        }

        private EditorGrid? grid;

        /// <summary>The editor's timing points, for repairs (<see cref="TimelineRepair.IGrid"/>).</summary>
        private sealed class EditorGrid : TimelineRepair.IGrid
        {
            private readonly KanComposeScreen screen;

            public EditorGrid(KanComposeScreen screen)
            {
                this.screen = screen;
            }

            /// <summary>The editor it belongs to is gone (the history outlives it): leave the grid alone.</summary>
            public bool Gone;

            private osu.Game.Screens.Edit.EditorBeatmap beatmap => screen.EditorBeatmap;

            private sealed record Point(double Time, double BeatLength, osu.Game.Beatmaps.Timing.TimeSignature Signature);

            public object Capture() => beatmap.ControlPointInfo.TimingPoints.Select(p => new Point(p.Time, p.BeatLength, p.TimeSignature)).ToList();

            public void Restore(object state)
            {
                if (!Gone) set((System.Collections.Generic.List<Point>)state);
            }

            public void Apply(TimelineEdit levelEdit)
            {
                if (Gone) return;

                set(((System.Collections.Generic.List<Point>)Capture()).Where(p => !levelEdit.Removes(p.Time))
                                                                          .Select(p => p with { Time = Math.Round(levelEdit.Map(p.Time), 1) })
                                                                          .ToList());
            }

            public System.Collections.Generic.IReadOnlyList<Kan.Core.Score.GridPoint> Points
                => beatmap.ControlPointInfo.TimingPoints.Select(p => new Kan.Core.Score.GridPoint { TimeMs = p.Time, BeatMs = p.BeatLength }).ToList();

            private void set(System.Collections.Generic.List<Point> points)
            {
                var cpi = beatmap.ControlPointInfo;
                beatmap.BeginChange();

                foreach (var tp in cpi.TimingPoints.ToList())
                {
                    var group = cpi.GroupAt(tp.Time);
                    group?.Remove(tp);
                    if (group != null && group.ControlPoints.Count == 0) cpi.RemoveGroup(group);
                }

                foreach (var p in points)
                    cpi.Add(p.Time, new osu.Game.Beatmaps.ControlPoints.TimingControlPoint { BeatLength = p.BeatLength, TimeSignature = p.Signature });

                beatmap.EndChange();
            }
        }

        /// <summary>A repair was undone or redone (here or in the track tab).</summary>
        private void repairRestored()
        {
            roll.Selected.Clear();
            roll.Invalidate();
            changed();
            renderLevelAudio();
        }

        /// <summary>The companion changed: the level audio (what the editor clock plays) is rendered again.</summary>
        private void renderLevelAudio()
        {
            var set = ForgeTrackSession.Tracks;
            if (!set.Companions.Any() || BeatmapAudio.IsUpToDate(set)) return;

            if (BeatmapAudio.Rendering)
            {
                // One render at a time: try again once this one is done.
                Scheduler.AddDelayed(renderLevelAudio, 500);
                return;
            }

            BeatmapAudio.RenderAsync(set, working.Value, beatmaps, music, editor, Scheduler)
                        .ContinueWith(t => Schedule(() =>
                        {
                            if (t.IsFaulted)
                                ForgeNotify.Error(notifications, $"Could not update the level audio: {t.Exception?.GetBaseException().Message}");
                            else
                                loadSpectrograms();
                        }));
        }
    }
}
