// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's track set for the open level: one unified list of
    /// resources — imported in Resources, arranged/edited in the "track"
    /// tab. Both surfaces read and write this set.
    ///
    /// Saved with its level (owner 2026-10-14): <see cref="Open"/> switches
    /// to a level's set (saving the previous one), changes are saved
    /// automatically (<see cref="AutoSave"/>), and every file the editor
    /// derives (stems, edits, renders, video audio) lives in the level's work
    /// folder instead of the system temp directory.
    /// </summary>
    public static class ForgeTrackSession
    {
        public static ForgeTrackSet Tracks { get; } = new();

        /// <summary>The track tab's undo/redo history (cleared when another level opens).</summary>
        public static ForgeSetHistory History { get; } = new ForgeSetHistory(Tracks);

        /// <summary>The last transcription and the track it came from (saved with the set).</summary>
        public static TranscriptionDraft? Draft { get; set; }

        public static string? DraftSource { get; set; }

        /// <summary>
        /// The instrument the level is written for — the transcriber's
        /// profile (owner 2026-10-14: an ocarina transcribed as "keyboard"
        /// resolved 97 of 609 notes; with its own profile, 546). Used by the
        /// track tab's Transcribe and the compose tab's section tools.
        /// </summary>
        public static string Instrument { get; set; } = @"keyboard";

        /// <summary>When the last transcription was made (the compose tab offers newer ones).</summary>
        public static DateTime DraftAtUtc { get; set; }

        /// <summary>What the level audio was last rendered from (<see cref="BeatmapAudio.Signature"/>).</summary>
        public static string? LevelAudioSignature { get; set; }

        /// <summary>The open level's key (beatmap set id), null before a level is opened.</summary>
        public static string? LevelKey { get; private set; }

        private static string root = Path.Combine(Path.GetTempPath(), "kan-forge");

        // Presentation: which tracks came out of the level's video, so both
        // surfaces can show the merge ("from video").
        private static readonly ConditionalWeakTable<ForgeTrack, object> video_audio = new();

        public static void MarkVideoAudio(ForgeTrack track) => video_audio.AddOrUpdate(track, track);

        public static bool IsVideoAudio(ForgeTrack track) => video_audio.TryGetValue(track, out _);

        /// <summary>The open level's work folder (derived files).</summary>
        public static string WorkDir => LevelKey == null ? root : Path.Combine(root, LevelKey);

        /// <summary>A path for a derived file in the open level's work folder.</summary>
        public static string WorkPath(string fileName)
        {
            Directory.CreateDirectory(WorkDir);
            return Path.Combine(WorkDir, fileName);
        }

        /// <summary>A unique path for a derived file: "{prefix}-xxxxxxxx{extension}".</summary>
        public static string NewWorkPath(string prefix, string extension = @".wav")
            => WorkPath($"{prefix}-{Guid.NewGuid().ToString("N")[..8]}{extension}");

        private static string setFile => Path.Combine(WorkDir, @"set.json");

        /// <summary>
        /// Switch to a level's set: save the current one, then load the
        /// level's saved set (or start empty). Returns the loaded tracks so
        /// the caller can re-analyse them (waveforms are not stored).
        /// No-op when the level is already open.
        /// </summary>
        public static IReadOnlyList<ForgeTrack> Open(string storageRoot, string levelKey)
        {
            ForgeTiming.Load(storageRoot);

            if (LevelKey == levelKey) return Array.Empty<ForgeTrack>();

            Save();

            root = storageRoot;
            LevelKey = levelKey;
            Tracks.Tracks.Clear();
            Tracks.AudioStartMs = 0;
            Tracks.AudioEndMs = 0;
            History.Clear();
            Draft = null;
            DraftSource = null;
            DraftAtUtc = default;
            Instrument = @"keyboard";
            LevelAudioSignature = null;
            lastSaved = null;
            pending = null;

            var doc = ForgeSetFile.Load(setFile);
            if (doc == null) return Array.Empty<ForgeTrack>();

            var loaded = new List<ForgeTrack>();
            Tracks.AudioStartMs = doc.AudioStartMs;
            Tracks.AudioEndMs = doc.AudioEndMs;
            LevelAudioSignature = doc.LevelAudioSignature;
            if (!string.IsNullOrEmpty(doc.Instrument)) Instrument = doc.Instrument;

            foreach (var (track, fromVideo) in ForgeSetFile.ToTracks(doc))
            {
                Tracks.Add(track);
                if (fromVideo) MarkVideoAudio(track);
                loaded.Add(track);
            }

            // A transcription saved before the 2026-10-14 fix arrived as zeros: not usable.
            bool zeroed = doc.TranscriptionNotes.Count > 0 && doc.TranscriptionNotes.All(n => n.OnsetMs == 0 && n.Midi == 0 && n.DurMs == 0);

            if (doc.TranscriptionNotes.Count > 0 && !zeroed)
            {
                Draft = new TranscriptionDraft(doc.TranscriptionNotes);
                DraftSource = doc.TranscriptionSource;
                DraftAtUtc = doc.TranscriptionAtUtc;
            }

            return loaded;
        }

        /// <summary>Write the open level's set now. Returns false (with the reason) if it could not be saved.</summary>
        public static bool Save(out string? error)
        {
            error = null;
            if (LevelKey == null) return true;

            try
            {
                var doc = ForgeSetFile.From(Tracks, IsVideoAudio, DraftSource, Draft);
                doc.LevelAudioSignature = LevelAudioSignature;
                doc.TranscriptionAtUtc = DraftAtUtc;
                doc.Instrument = Instrument;
                ForgeSetFile.Save(doc, setFile);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static void Save() => Save(out _);

        private static string? lastSaved;
        private static string? pending;
        private static double pendingSince;

        /// <summary>
        /// Save shortly after the set stops changing. Call every poll with
        /// the set's current signature and a clock (ms).
        /// </summary>
        public static void AutoSave(string signature, double nowMs)
        {
            if (LevelKey == null || signature == lastSaved) return;

            if (signature != pending)
            {
                pending = signature;
                pendingSince = nowMs;
                return;
            }

            if (nowMs - pendingSince < 1000) return;

            if (Save(out _))
                lastSaved = signature;
        }

        /// <summary>Mark the current state as saved (after an explicit save).</summary>
        public static void MarkSaved(string signature) => lastSaved = signature;
    }
}
