// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kan.Core.Transcription;

namespace Kan.Core.Score
{
    /// <summary>How a note is played (judged and shown by the game).</summary>
    public enum Articulation
    {
        /// <summary>A normal attacked note.</summary>
        Normal,

        /// <summary>Connected to the previous note, without a new attack.</summary>
        Legato,

        /// <summary>Short and detached.</summary>
        Staccato,

        /// <summary>Attacked with emphasis.</summary>
        Accent,
    }

    /// <summary>
    /// One note of the score — what gameplay judges (owner 2026-10-14: timing,
    /// correct note, intensity, play style). Times are on the level audio's
    /// timeline (ms).
    /// </summary>
    public class ScoreNote
    {
        public double StartMs { get; set; }

        public double DurationMs { get; set; } = 250;

        /// <summary>MIDI note number (60 = C4).</summary>
        public int Midi { get; set; } = 60;

        /// <summary>Intonation offset from the equal-tempered pitch, in cents.</summary>
        public float Cents { get; set; }

        /// <summary>0 (softest) .. 1 (loudest).</summary>
        public float Intensity { get; set; } = 0.6f;

        public Articulation Articulation { get; set; }

        public bool Vibrato { get; set; }

        /// <summary>
        /// The transcriber heard a note here but could not tell its pitch; the
        /// pitch is a guess (its neighbour's) to be fixed. Cleared as soon as
        /// the pitch is set by hand.
        /// </summary>
        public bool PitchUnknown { get; set; }

        [JsonIgnore]
        public double EndMs => StartMs + DurationMs;

        public ScoreNote Clone() => (ScoreNote)MemberwiseClone();
    }

    /// <summary>A timing point of the level's tempo grid, as the score remembers it.</summary>
    public class GridPoint
    {
        public double TimeMs { get; set; }

        /// <summary>One beat (ms).</summary>
        public double BeatMs { get; set; }
    }

    /// <summary>
    /// The level's score (owner decision 2026-10-14): the notes gameplay
    /// judges, stored as a versioned file inside the level so it travels with
    /// it (<see cref="FILE_NAME"/>). The osu!-format file can't carry pitch or
    /// duration. Every editing operation of the compose tab lives here so it
    /// is testable without the UI.
    /// </summary>
    public class KanScore : ILevelDocument
    {
        public const int VERSION = 1;

        /// <summary>The score's file name inside the level.</summary>
        public const string FILE_NAME = @"kan.score.json";

        public const double MIN_DURATION_MS = 10;

        public int Version { get; set; } = VERSION;

        /// <summary>The instrument profile the score is written for (transcription profile name).</summary>
        public string Instrument { get; set; } = @"keyboard";

        public List<ScoreNote> Notes { get; set; } = new List<ScoreNote>();

        /// <summary>When the transcription this score started from was made (default: none).</summary>
        public DateTime TranscriptionAtUtc { get; set; }

        /// <summary>When this copy was written (picks the newer of the level's copy and the editor's autosave).</summary>
        public DateTime SavedAtUtc { get; set; }

        /// <summary>
        /// The tempo grid (the level's timing points) the notes were last
        /// placed on; null until first seen (scores written before it).
        /// See <see cref="FollowGrid"/>.
        /// </summary>
        public List<GridPoint>? Grid { get; set; }

        public KanScore Clone() => new KanScore
        {
            Version = Version,
            Instrument = Instrument,
            SavedAtUtc = SavedAtUtc,
            TranscriptionAtUtc = TranscriptionAtUtc,
            Notes = Notes.Select(n => n.Clone()).ToList(),
            Grid = Grid?.Select(p => new GridPoint { TimeMs = p.TimeMs, BeatMs = p.BeatMs }).ToList(),
        };

        /// <summary>
        /// Keep the notes on their beats when the level's tempo grid changed
        /// (owner 2026-10-22: timing changes did not reach compose — the notes
        /// are kept in ms in this file, not as osu! objects, so osu!'s "adjust
        /// existing objects on timing changes" never moved them). Like osu!: a
        /// timing point moved carries the notes of its section by as much; a
        /// new tempo keeps each note on its beat, counted from the point (its
        /// length scales too). The first section reaches back to the start.
        /// Points added or removed move nothing (each point is matched with
        /// the one in its place). <paramref name="move"/> false (the option
        /// off): the grid is taken as it is. Undo snapshots carry their own
        /// grid, so an older score follows the grid of now when it comes back.
        /// </summary>
        /// <summary>A grid as one comparable string.</summary>
        public static string GridKey(IEnumerable<GridPoint> grid)
            => string.Join(';', grid.Select(p => p.TimeMs.ToString("0.###", CultureInfo.InvariantCulture) + ":" + p.BeatMs.ToString("0.######", CultureInfo.InvariantCulture)));

        /// <returns>Whether any note moved.</returns>
        public bool FollowGrid(IReadOnlyList<GridPoint> grid, bool move = true)
        {
            var old = Grid;
            Grid = grid.Select(p => new GridPoint { TimeMs = p.TimeMs, BeatMs = p.BeatMs }).ToList();

            if (!move || old == null || old.Count != grid.Count || old.Count == 0) return false;

            bool same = true;

            for (int i = 0; i < old.Count && same; i++)
                same = old[i].TimeMs == grid[i].TimeMs && old[i].BeatMs == grid[i].BeatMs;

            // The same music counted in another note value (every beat ×2, ×4, ½…:
            // GridTempoBar.ScaleBeat, or that undone): nothing moves (owner
            // 2026-10-23: a level put from 130 to 32.5 BPM, its timing undone
            // to 65 elsewhere, played with its notes squeezed to half).
            if (same || countedOtherwise(old, grid)) return false;

            bool moved = false;

            foreach (var n in Notes)
            {
                int section = 0;
                while (section + 1 < old.Count && old[section + 1].TimeMs <= n.StartMs + 0.5) section++;

                var from = old[section];
                var to = grid[section];
                double ratio = from.BeatMs > 0 && to.BeatMs > 0 ? to.BeatMs / from.BeatMs : 1;

                double start = to.TimeMs + (n.StartMs - from.TimeMs) * ratio;
                double duration = Math.Max(MIN_DURATION_MS, n.DurationMs * ratio);

                moved |= Math.Abs(start - n.StartMs) > 0.01 || Math.Abs(duration - n.DurationMs) > 0.01;
                n.StartMs = start;
                n.DurationMs = duration;
            }

            Sort();
            return moved;
        }

        /// <summary>Whether two grids have the same points with every beat a power of two of the other's.</summary>
        private static bool countedOtherwise(IReadOnlyList<GridPoint> a, IReadOnlyList<GridPoint> b)
        {
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i].TimeMs - b[i].TimeMs) > 0.05 || a[i].BeatMs <= 0 || b[i].BeatMs <= 0) return false;

                double power = Math.Log2(b[i].BeatMs / a[i].BeatMs);
                if (Math.Abs(power - Math.Round(power)) > 1e-6) return false;
            }

            return true;
        }

        public void Sort() => Notes.Sort((a, b) => a.StartMs != b.StartMs ? a.StartMs.CompareTo(b.StartMs) : a.Midi.CompareTo(b.Midi));

        // ------------------------------------------------------------------
        // From a transcription
        // ------------------------------------------------------------------

        /// <summary>A score from a transcription draft (the measured notes become the starting point).</summary>
        public static KanScore FromDraft(TranscriptionDraft draft, string instrument, double offsetMs = 0)
        {
            var score = new KanScore { Instrument = instrument };

            var notes = draft.Notes.OrderBy(n => n.OnsetMs).ToList();
            var open = new HashSet<ScoreNote>();
            float audioEnd = notes.Count > 0 ? notes.Max(n => n.EndMs) : 0;

            for (int i = 0; i < notes.Count; i++)
            {
                var note = FromDraftNote(notes[i], offsetMs);

                // Heard, but its pitch unresolved: keep it (its timing is real),
                // guess the nearest resolved neighbour's pitch, and mark it.
                if (notes[i].Midi < 0)
                {
                    note.Midi = nearestResolvedPitch(notes, i) ?? 60;
                    note.PitchUnknown = true;
                }

                if (notes.Count > 1 && notes[i].EndMs >= audioEnd - 1)
                    open.Add(note);

                score.Notes.Add(note);
            }

            bool monophonic = IsMonophonic(instrument);
            var repeats = score.FindRepeats(monophonic).ToHashSet();
            score.Notes.RemoveAll(repeats.Contains);
            endOpenNotes(score.Notes, open, monophonic);

            score.Sort();
            return score;
        }

        /// <summary>Instruments that sound one note at a time (one air column / one bow).</summary>
        /// <summary>
        /// The notes that start while another still sounds — two pitches at once,
        /// which an instrument that plays one note at a time cannot (owner
        /// 2026-10-25). Each with the note it overlaps. Overlaps shorter than
        /// <paramref name="seamMs"/> are a legato's seam, not two notes at once.
        /// </summary>
        public IEnumerable<(ScoreNote Sounding, ScoreNote Starting)> SoundingTogether(double seamMs = 20)
        {
            ScoreNote? sounding = null;

            foreach (var n in Notes.OrderBy(n => n.StartMs))
            {
                if (sounding != null && n.StartMs < sounding.EndMs - seamMs)
                {
                    yield return (sounding, n);
                    if (n.EndMs <= sounding.EndMs) continue; // the longer one still sounds
                }

                sounding = n;
            }
        }

        public static bool IsMonophonic(string instrument) => instrument is @"ocarina" or @"flute" or @"recorder" or @"wind_bowed";

        /// <summary>
        /// The transcriber sometimes finds no release and lets a note run to the
        /// end of the audio (seen 2026-10-14: 60 of 617 ocarina notes lasting
        /// minutes). Such an "open" note ends where the next note starts; on a
        /// one-note-at-a-time instrument every note does.
        /// </summary>
        private static void endOpenNotes(List<ScoreNote> notes, HashSet<ScoreNote> open, bool monophonic)
        {
            for (int i = 0; i + 1 < notes.Count; i++)
            {
                var note = notes[i];
                if (!open.Contains(note) && !monophonic) continue;

                // The next note that starts later (notes can share an onset).
                var next = notes.Skip(i + 1).FirstOrDefault(n => n.StartMs > note.StartMs);
                if (next == null) continue;

                double room = next.StartMs - note.StartMs;
                if (note.DurationMs > room)
                    note.DurationMs = Math.Max(MIN_DURATION_MS, room);
            }
        }

        /// <summary>Notes starting closer than this to another are one sound heard twice.</summary>
        public const double REPEAT_WINDOW_MS = 30;

        /// <summary>
        /// Notes that repeat another one: the transcriber sometimes reports
        /// one sound twice (seen 2026-10-14: 57 of 617 ocarina notes were a
        /// second copy of a note at the same moment). A repeat starts within
        /// <see cref="REPEAT_WINDOW_MS"/> of a kept note with the same pitch;
        /// on a one-note-at-a-time instrument, any note (pitch known or not). The kept one is the
        /// one with a known pitch, then the longer one. Returned, not removed,
        /// so the editor can make it one undoable step.
        /// </summary>
        public List<ScoreNote> FindRepeats(bool monophonic)
        {
            var repeats = new List<ScoreNote>();
            var kept = new List<ScoreNote>();

            var ordered = Notes.OrderBy(n => n.StartMs)
                               .ThenBy(n => n.PitchUnknown)
                               .ThenByDescending(n => n.DurationMs)
                               .ToList();

            foreach (var note in ordered)
            {
                kept.RemoveAll(k => note.StartMs - k.StartMs > REPEAT_WINDOW_MS);

                // Elsewhere, another pitch at the same moment is a chord; an unknown
                // pitch (its pitch is only a guess) may be a chord note: both stay.
                var twin = kept.FirstOrDefault(k => monophonic || (k.Midi == note.Midi && k.PitchUnknown == note.PitchUnknown));

                if (twin != null)
                {
                    if (preferred(note, twin))
                    {
                        repeats.Add(twin);
                        kept.Remove(twin);
                        kept.Add(note);
                    }
                    else
                        repeats.Add(note);

                    continue;
                }

                kept.Add(note);
            }

            return repeats;

            static bool preferred(ScoreNote a, ScoreNote b)
                => a.PitchUnknown != b.PitchUnknown ? !a.PitchUnknown : a.DurationMs > b.DurationMs;
        }

        private static int? nearestResolvedPitch(List<KanDraftNote> notes, int index)
        {
            for (int d = 1; d < notes.Count; d++)
            {
                if (index - d >= 0 && notes[index - d].Midi >= 0) return notes[index - d].Midi;
                if (index + d < notes.Count && notes[index + d].Midi >= 0) return notes[index + d].Midi;
                if (index - d < 0 && index + d >= notes.Count) break;
            }

            return null;
        }

        public static ScoreNote FromDraftNote(KanDraftNote n, double offsetMs = 0) => new ScoreNote
        {
            StartMs = offsetMs + n.OnsetMs,
            DurationMs = Math.Max(MIN_DURATION_MS, n.DurMs > 0 ? n.DurMs : n.EndMs - n.OnsetMs),
            Midi = Math.Clamp(n.Midi, 0, 127),
            Cents = n.Cents,
            // -60 dB (barely there) .. 0 dB (full) → 0..1
            Intensity = Math.Clamp((n.DynDb + 60) / 60, 0, 1),
            Articulation = n.Transition ? Articulation.Legato : Articulation.Normal,
            Vibrato = n.VibDepth > 0,
        };

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        /// <summary>
        /// Move notes together (owner 2026-10-14: align groups of notes with
        /// the song). The group keeps its shape; it stops at 0 ms and at the
        /// MIDI range instead of deforming.
        /// </summary>
        public void Move(IReadOnlyCollection<ScoreNote> notes, double deltaMs, int deltaSemitones)
        {
            if (notes.Count == 0) return;

            deltaMs = Math.Max(deltaMs, -notes.Min(n => n.StartMs));
            deltaSemitones = Math.Clamp(deltaSemitones, -notes.Min(n => n.Midi), 127 - notes.Max(n => n.Midi));

            foreach (var n in notes)
            {
                n.StartMs += deltaMs;
                n.Midi += deltaSemitones;

                // A pitch set by hand is no longer a guess.
                if (deltaSemitones != 0) n.PitchUnknown = false;
            }
        }

        /// <summary>Lengthen or shorten notes by the same amount (never below <see cref="MIN_DURATION_MS"/>).</summary>
        public void Resize(IReadOnlyCollection<ScoreNote> notes, double deltaMs)
        {
            foreach (var n in notes)
                n.DurationMs = Math.Max(MIN_DURATION_MS, n.DurationMs + deltaMs);
        }

        /// <summary>
        /// Remove every note outside [<paramref name="lowMidi"/>,
        /// <paramref name="highMidi"/>] (owner example: keep G4–A8 for a
        /// piano's right hand). Returns the removed notes so they can be
        /// offered back one by one.
        /// </summary>
        public List<ScoreNote> KeepRange(int lowMidi, int highMidi)
        {
            if (lowMidi > highMidi) (lowMidi, highMidi) = (highMidi, lowMidi);

            var removed = Notes.Where(n => n.Midi < lowMidi || n.Midi > highMidi).ToList();
            Notes.RemoveAll(n => n.Midi < lowMidi || n.Midi > highMidi);
            return removed;
        }

        /// <summary>
        /// Replace everything that starts inside [<paramref name="fromMs"/>,
        /// <paramref name="toMs"/>) with <paramref name="replacement"/> (a
        /// section recorded with the mic or re-transcribed). Replacement notes
        /// outside the section are dropped.
        /// </summary>
        public void ReplaceSection(double fromMs, double toMs, IEnumerable<ScoreNote> replacement)
        {
            if (toMs < fromMs) (fromMs, toMs) = (toMs, fromMs);

            Notes.RemoveAll(n => n.StartMs >= fromMs && n.StartMs < toMs);
            Notes.AddRange(replacement.Where(n => n.StartMs >= fromMs && n.StartMs < toMs).Select(n => n.Clone()));
            Sort();
        }

        // ------------------------------------------------------------------
        // Note names
        // ------------------------------------------------------------------

        private static readonly string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        /// <summary>"C4" for 60 (scientific pitch notation, sharps).</summary>
        public static string NoteName(int midi) => $"{names[((midi % 12) + 12) % 12]}{midi / 12 - 1}";

        /// <summary>Parse "G4", "a#3", "Bb5", "C-1" → MIDI; false when it isn't a note name.</summary>
        public static bool TryParseNote(string? text, out int midi)
        {
            midi = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim();
            int letter = "C D EF G A B".IndexOf(char.ToUpperInvariant(text[0]));
            if (letter < 0 || text[0] == ' ') return false;

            int i = 1, accidental = 0;
            if (i < text.Length && (text[i] == '#' || text[i] == '♯')) { accidental = 1; i++; }
            else if (i < text.Length && (text[i] == 'b' || text[i] == '♭')) { accidental = -1; i++; }

            if (!int.TryParse(text[i..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int octave))
                return false;

            midi = (octave + 1) * 12 + letter + accidental;
            return midi >= 0 && midi <= 127;
        }

        // ------------------------------------------------------------------
        // File
        // ------------------------------------------------------------------

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, options);

        /// <summary>The score's content, without its save time (to compare two copies).</summary>
        public string ContentKey()
        {
            var copy = Clone();
            copy.SavedAtUtc = default;
            return copy.ToJson();
        }

        /// <summary>Stamp the save time and serialize.</summary>
        public string ToJsonStamped()
        {
            SavedAtUtc = DateTime.UtcNow;
            return ToJson();
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJsonStamped());
            File.Move(tmp, path, true);
        }

        /// <summary>Read a score; null when it is missing, unreadable or from a newer version.</summary>
        public static KanScore? FromJson(string json)
        {
            try
            {
                var score = JsonSerializer.Deserialize<KanScore>(json, options);
                if (score == null || score.Version > VERSION) return null;

                score.Notes ??= new List<ScoreNote>();
                score.Sort();
                return score;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static KanScore? Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
    }

    /// <summary>Undo/redo for the score (snapshots; changes with the same key within a second merge).</summary>
    public class ScoreHistory
    {
        private const int max_steps = 200;

        private readonly List<(KanScore Score, object? Link)> undo = new List<(KanScore, object?)>();
        private readonly List<(KanScore Score, object? Link)> redo = new List<(KanScore, object?)>();
        private string? lastKey;
        private DateTime lastRecord;

        public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        public bool CanUndo => undo.Count > 0;

        public bool CanRedo => redo.Count > 0;

        /// <summary>
        /// The next undo's link: a step taken together with another history
        /// (a timeline repair is also a step of the track set's history), or null.
        /// </summary>
        public object? UndoLink => undo.Count > 0 ? undo[^1].Link : null;

        public object? RedoLink => redo.Count > 0 ? redo[^1].Link : null;

        /// <summary>Call BEFORE changing <paramref name="score"/>. <paramref name="link"/>: the step is also taken in another history (<see cref="UndoLink"/>).</summary>
        public void Record(KanScore score, string? coalesceKey = null, object? link = null)
        {
            var now = Now();
            if (link == null && coalesceKey != null && coalesceKey == lastKey && now - lastRecord < TimeSpan.FromSeconds(1))
            {
                lastRecord = now;
                return;
            }

            lastKey = coalesceKey;
            lastRecord = now;
            undo.Add((score.Clone(), link));
            if (undo.Count > max_steps) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>The score to show after undoing, or null when there is nothing to undo.</summary>
        public KanScore? Undo(KanScore current) => step(undo, redo, current);

        public KanScore? Redo(KanScore current) => step(redo, undo, current);

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
            lastKey = null;
        }

        private KanScore? step(List<(KanScore Score, object? Link)> from, List<(KanScore Score, object? Link)> to, KanScore current)
        {
            if (from.Count == 0) return null;

            var target = from[^1];
            from.RemoveAt(from.Count - 1);
            to.Add((current.Clone(), target.Link));
            lastKey = null;
            return target.Score;
        }
    }
}
