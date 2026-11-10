// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kan.Core.Transcription;

namespace Kan.Core.Tracks
{
    /// <summary>
    /// The set saved with its level (owner 2026-10-14: the set survives a
    /// restart). One JSON file per level: tracks with their roles, volume,
    /// mute, start, pending edits and material flags, plus the last
    /// transcription. Analysis results (waveform, "no meaningful audio") are
    /// recomputed on load, never stored.
    /// </summary>
    public static class ForgeSetFile
    {
        public const int VERSION = 1;

        /// <summary>The set's file in the level's kan-forge folder.</summary>
        public const string FILE_NAME = @"set.json";

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
        };

        public class TrackEntry
        {
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public float Gain { get; set; } = 1;
            public bool Muted { get; set; }
            public double OffsetMs { get; set; }
            public double ClipStartMs { get; set; }
            public double ClipEndMs { get; set; }
            public bool IsReference { get; set; }
            public bool IsCompanion { get; set; }

            // Sets saved before the naming convention (2026-10-14): the old
            // "accompaniment" and "music" roles both meant "plays as the
            // level audio": they read as companion. Never written.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool IsAccompaniment { get => false; set { if (value) IsCompanion = true; } }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool IsMusic { get => false; set { if (value) IsCompanion = true; } }
            public bool DemuxPending { get; set; }
            public bool IsSeparated { get; set; }
            public bool IsMixdown { get; set; }
            public bool FromVideo { get; set; }
            public TrackEdits Edits { get; set; } = new TrackEdits();
        }

        public class Document
        {
            public int Version { get; set; } = VERSION;
            public List<TrackEntry> Tracks { get; set; } = new List<TrackEntry>();

            public double AudioStartMs { get; set; }
            public double AudioEndMs { get; set; }

            /// <summary>The level audio's loudness from the game's reference (dB, <see cref="LevelLoudness"/>).</summary>
            public double LoudnessOffsetDb { get; set; }

            // Saved before the naming convention: the "song" window.
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public double SongStartMs { get => 0; set => AudioStartMs = value; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public double SongEndMs { get => 0; set => AudioEndMs = value; }

            /// <summary>What the level audio was last rendered from (skip re-rendering an unchanged mix).</summary>
            public string? LevelAudioSignature { get; set; }

            /// <summary>The level audio's loudness as last rendered (LUFS).</summary>
            public double? LevelAudioLufs { get; set; }

            /// <summary>Path of the track the transcription was made from (null = none).</summary>
            public string? TranscriptionSource { get; set; }

            public List<KanDraftNote> TranscriptionNotes { get; set; } = new List<KanDraftNote>();

            /// <summary>The instrument the level is written for (the transcriber's profile).</summary>
            public string? Instrument { get; set; }

            /// <summary>When the transcription was made.</summary>
            public DateTime TranscriptionAtUtc { get; set; }
        }

        /// <summary>Snapshot the set (and the last transcription) into a document.</summary>
        public static Document From(ForgeTrackSet set, Func<ForgeTrack, bool> fromVideo,
                                    string? transcriptionSource = null, TranscriptionDraft? draft = null)
            => new Document
            {
                Tracks = set.Tracks.Select(t => new TrackEntry
                {
                    Name = t.Name,
                    Path = t.Path,
                    Gain = t.Gain,
                    Muted = t.Muted,
                    OffsetMs = t.OffsetMs,
                    ClipStartMs = t.ClipStartMs,
                    ClipEndMs = t.ClipEndMs,
                    IsReference = t.IsReference,
                    IsCompanion = t.IsCompanion,
                    DemuxPending = t.DemuxPending,
                    IsSeparated = t.IsSeparated,
                    IsMixdown = t.IsMixdown,
                    FromVideo = fromVideo(t),
                    Edits = t.Edits.Clone(),
                }).ToList(),
                AudioStartMs = set.AudioStartMs,
                AudioEndMs = set.AudioEndMs,
                LoudnessOffsetDb = set.LoudnessOffsetDb,
                TranscriptionSource = draft != null ? transcriptionSource : null,
                TranscriptionNotes = draft?.Notes.Select(n => n.Clone()).ToList() ?? new List<KanDraftNote>(),
            };

        /// <summary>Recreate the tracks of a document (the caller adds them to its set).</summary>
        public static List<(ForgeTrack Track, bool FromVideo)> ToTracks(Document doc)
            => doc.Tracks.Select(e => (new ForgeTrack(e.Name, e.Path)
            {
                Gain = e.Gain,
                Muted = e.Muted,
                ClipStartMs = e.ClipStartMs, // before OffsetMs: it bounds it
                OffsetMs = e.OffsetMs,
                ClipEndMs = e.ClipEndMs,
                IsReference = e.IsReference,
                IsCompanion = e.IsCompanion,
                DemuxPending = e.DemuxPending,
                IsSeparated = e.IsSeparated,
                IsMixdown = e.IsMixdown,
                Edits = e.Edits ?? new TrackEdits(),
            }, e.FromVideo)).ToList();

        public static void Save(Document doc, string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            // Write-then-rename: a crash mid-save never leaves a torn file.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, options));
            File.Move(tmp, path, true);
        }

        /// <summary>Load a saved set, or null when there is none (or it can't be read).</summary>
        public static Document? Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), options);
                return doc is { Version: <= VERSION } ? doc : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
