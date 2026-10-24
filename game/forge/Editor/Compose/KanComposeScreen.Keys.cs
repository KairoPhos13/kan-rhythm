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
    /// The compose tab's keys, and copy and paste (choruses are often alike).
    /// </summary>
    public partial class KanComposeScreen : EditorScreen, IKeyBindingHandler<PlatformAction>, SectionPanel.ITools
    {
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
            var tempos = EditorBeatmap.ControlPointInfo.TimingPoints.Select(p => new Bars.Tempo(p.Time, p.BeatLength, p.TimeSignature.Numerator)).ToList();

            // Land on the bar lines the piano roll draws (Bars).
            var bars = Bars.Of(tempos, clock.TrackLength);
            double? target = forward ? bars.Cast<double?>().FirstOrDefault(b => b > now + 1) : bars.Cast<double?>().LastOrDefault(b => b < now - 1);
            if (target is double to) clock.SeekSmoothlyTo(Math.Clamp(to, 0, clock.TrackLength));
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

        /// <summary>
        /// The notes copied, from the first one's start (kept while the game
        /// runs, so a phrase can go to another level's score too).
        /// </summary>
        private static readonly List<ScoreNote> clipboard = new List<ScoreNote>();

        public override void Copy()
        {
            if (roll.Selected.Count == 0) return;

            double from = roll.Selected.Min(n => n.StartMs);
            clipboard.Clear();

            foreach (var n in roll.Selected.OrderBy(n => n.StartMs).ThenBy(n => n.Midi))
            {
                var copy = n.Clone();
                copy.StartMs -= from;
                clipboard.Add(copy);
            }
        }

        public override void Cut()
        {
            if (roll.Selected.Count == 0) return;

            Copy();
            deleteSelection();
        }

        /// <summary>The copied notes at the playhead (on the grid when snapping), selected so they can be moved as one.</summary>
        public override void Paste()
        {
            if (clipboard.Count == 0) return;

            double at = Math.Max(0, roll.Snapped(clock.CurrentTime));

            record(null);
            roll.Selected.Clear();

            foreach (var n in clipboard)
            {
                var pasted = n.Clone();
                pasted.StartMs += at;
                ScoreSession.Score.Notes.Add(pasted);
                roll.Selected.Add(pasted);
            }

            ScoreSession.Score.Sort();
            roll.Invalidate();
            changed();
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

        // Copy and paste (owner 2026-10-23: choruses are often alike)
        // ------------------------------------------------------------------
    }
}
