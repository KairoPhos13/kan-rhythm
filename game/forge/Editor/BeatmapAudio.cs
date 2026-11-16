// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using System.Threading.Tasks;
using Kan.Core.Native;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level audio (owner naming 2026-10-14): the beatmap's single audio
    /// file is the mix of the companion tracks. Rendering it and installing
    /// it as the beatmap's audio happens only here.
    ///
    /// Mirrors the stock Audio Track flow after the file swap: save the
    /// beatmap (so the change survives leaving the editor) and reload the
    /// current track (so the editor clock and the playback bar play it).
    /// </summary>
    public static class BeatmapAudio
    {
        /// <summary>Returns the beatmap file name, or null (with <paramref name="error"/>) on failure.</summary>
        public static string? Apply(WorkingBeatmap working, BeatmapManager beatmaps, MusicController music, osu.Game.Screens.Edit.Editor? editor,
                                    string sourcePath, out string? error, bool replaceRendered = false, string? targetName = null)
        {
            error = null;

            try
            {
                var set = working.BeatmapSetInfo;

                string name;

                if (targetName != null)
                    name = targetName; // a fixed name: adding it again replaces the file
                else
                {
                    name = @"audio-render.wav";
                    if (!ForgeTrack.LooksLikeMixdown(sourcePath))
                        name = Path.GetFileName(sourcePath);

                    name = FreeFileName(name, set.Files.Select(f => f.Filename));
                }

                using (var stream = File.OpenRead(sourcePath))
                    beatmaps.AddFile(set, stream, name);

                string previous = working.BeatmapInfo.Metadata.AudioFile;
                working.BeatmapInfo.Metadata.AudioFile = name;

                // Save first, and only then remove the render this one
                // replaces (2026-11-12 audit B7): the save goes through a
                // guarded reflection call that swallows failure, and a level
                // that still has its old file plays on, while one that lost
                // it with the metadata change unkept is silent. Imported
                // audio files are never deleted; renders are ours to remove.
                bool saved = SaveEditor(editor);

                if (saved && replaceRendered && isRender(previous) && !SameFile(previous, name) && set.GetFile(previous) is { } old)
                    beatmaps.DeleteFile(set, old);

                music.ReloadCurrentTrack();
                return name;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>The level audio's file name: named after the level ("My Song - level audio.ogg").</summary>
        public static string LevelAudioFileName(WorkingBeatmap working)
        {
            string title = working.BeatmapInfo.Metadata.Title?.Trim() ?? string.Empty;
            string safe = new string(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\').ToArray()).Trim();

            return string.IsNullOrEmpty(safe) ? LEVEL_AUDIO_SUFFIX.TrimStart(' ', '-') : safe + LEVEL_AUDIO_SUFFIX;
        }

        /// <summary>
        /// The format the level's played files are written in (issue 42):
        /// Ogg Vorbis, about a ninth of the WAV they were before. Levels
        /// written as WAV keep playing; their next render writes Ogg.
        /// </summary>
        public const string PLAY_FILE_EXTENSION = @".ogg";

        /// <summary>The Vorbis quality of the played files (libvorbis VBR, about 190 kbit/s stereo).</summary>
        public const float PLAY_FILE_QUALITY = 0.6f;

        private const string level_audio_stem = @"level audio";
        private const string LEVEL_AUDIO_SUFFIX = @" - " + level_audio_stem + PLAY_FILE_EXTENSION;

        /// <summary>The level's voice file: named after the level ("My Song - level voice.ogg").</summary>
        public static string LevelVoiceFileName(WorkingBeatmap working)
            => LevelAudioFileName(working).Replace(level_audio_stem + PLAY_FILE_EXTENSION, LEVEL_VOICE_NAME, StringComparison.Ordinal);

        /// <summary>How a level voice file is named now; earlier levels end in "level voice.wav".</summary>
        public const string LEVEL_VOICE_NAME = level_voice_stem + PLAY_FILE_EXTENSION;

        private const string level_voice_stem = @"level voice";

        /// <summary>Whether a file of a level is its voice file (Ogg, or WAV from before issue 42).</summary>
        public static bool IsLevelVoice(string? fileName) => isLevelFile(fileName, level_voice_stem);

        private static bool isLevelFile(string? fileName, string stem)
            => !string.IsNullOrEmpty(fileName)
               && Path.GetFileNameWithoutExtension(fileName).EndsWith(stem, StringComparison.OrdinalIgnoreCase)
               && Path.GetExtension(fileName).ToLowerInvariant() is @".ogg" or @".wav";

        /// <summary>Whether two file names name the same file: the level's file store ignores case (audit B7).</summary>
        public static bool SameFile(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A file name that is free in the set: <paramref name="name"/>, or
        /// one with a counter before the extension while the name is taken
        /// (compared ignoring case, like the file store).
        /// </summary>
        public static string FreeFileName(string name, IEnumerable<string> existing)
        {
            string free = name;

            for (int i = 1; existing.Any(f => SameFile(f, free)); i++)
                free = Path.GetFileNameWithoutExtension(name) + $"-{i}" + Path.GetExtension(name);

            return free;
        }

        private static bool isRender(string? fileName)
            => !string.IsNullOrEmpty(fileName)
               && (isLevelFile(fileName, level_audio_stem)
                   || fileName.StartsWith(@"level-audio", StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(@"audio-render", StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(@"render-", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The stock flow saves after a resource change, via the editor's
        /// internal <c>Save()</c>. Plugin code reaches it by a guarded
        /// reflection call (same pattern as hiding the stock Audio Track
        /// row): if an upstream rename breaks it, the save is skipped (the
        /// track still switches), and the user's normal save persists it.
        /// Returns whether the save ran (audit B7: the caller may only remove
        /// a replaced render once the change is safely stored).
        /// </summary>
        public static bool SaveEditor(osu.Game.Screens.Edit.Editor? editor)
        {
            if (editor == null) return false;

            try
            {
                var save = typeof(osu.Game.Screens.Edit.Editor)
                    .GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes);

                if (save == null) return false; // an upstream rename: nothing was saved

                save.Invoke(editor, null);
                return true;
            }
            catch
            {
                // Never break the connection over persistence.
                return false;
            }
        }

        // ------------------------------------------------------------------
        // The level audio = the companion mix (owner naming 2026-10-14)
        // ------------------------------------------------------------------

        /// <summary>Everything the level audio is made of, as one comparable string.</summary>
        public static string Signature(ForgeTrackSet set)
        {
            // Invariant numbers (audit B13): a signature that changes with
            // the machine's locale would re-render the level audio on every
            // save after a locale change.
            var parts = set.Companions.Select(t =>
                FormattableString.Invariant($"{t.Path}|{(t.IsVoice ? "V" : "")}{t.Gain:0.000}|{t.OffsetMs:0}|{t.ClipStartMs:0}-{t.ClipEndMs:0}|{t.Edits.LowCutHz:0}/{t.Edits.HighCutHz:0}^{t.Edits.PitchSemitones}|")
                + string.Join(';', t.Edits.Ranges.Select(r => FormattableString.Invariant($"{(int)r.Mode}:{r.FromMs:0}-{r.ToMs:0}"))));

            // The levelling too: a level rendered before it, or at another offset, is rendered again.
            // And the format: a level written as WAV is written again as Ogg (issue 42).
            return FormattableString.Invariant($"{set.AudioStartMs:0}-{set.AudioEndMs:0}#L{LevelLoudness.VERSION}:{set.LoudnessOffsetDb:0.0}#F{PLAY_FILE_EXTENSION}#")
                   + string.Join('#', parts);
        }

        /// <summary>The level audio matches the companion mix as it is now.</summary>
        public static bool IsUpToDate(ForgeTrackSet set) => ForgeTrackSession.LevelAudioSignature == Signature(set);

        /// <summary>A render is running (one at a time).</summary>
        public static bool Rendering { get; private set; }

        /// <summary>
        /// Render the companion tracks (as heard: edits, filters, trims,
        /// positions) cut to the level audio window, and install the result
        /// as the level's audio. Call from the update thread; returns the
        /// beatmap file name.
        /// </summary>
        public static async Task<string> RenderAsync(ForgeTrackSet set, WorkingBeatmap working, BeatmapManager beatmaps,
                                                     MusicController music, osu.Game.Screens.Edit.Editor? editor, Scheduler scheduler)
        {
            var companions = set.Companions.ToList();
            if (companions.Count == 0)
                throw new ForgeException("no companion tracks: mark at least one track as companion");

            if (Rendering)
                throw new ForgeException("the level audio is already being rendered");

            string signature = Signature(set);
            double start = set.AudioStartMs, end = set.AudioEndMs;
            bool cut = set.HasAudioWindow;

            Rendering = true;

            try
            {
                // The whole companion mix sets the level's loudness, voice
                // included: the player hears it whole unless they turn the
                // voice off.
                var (full, before, after) = await renderLevelled(companions, set.LoudnessOffsetDb, start, end, cut, scheduler);

                // A level with a voice (owner 2026-11-16): the level audio is
                // the rest, the voice its own file, both at the gain the whole
                // mix took, so together they sound exactly as the whole.
                var voices = set.Voices.ToList();
                var instrumental = set.Instrumental.ToList();
                string levelAudio = full;
                string? voiceFile = null;

                if (voices.Count > 0 && instrumental.Count > 0)
                {
                    double gain = double.IsFinite(before) && double.IsFinite(after) ? Math.Pow(10, (after - before) / 20) : 1;
                    levelAudio = await renderAtGain(instrumental, gain, @"level-audio-instrumental", start, end, cut, scheduler);
                    voiceFile = await renderAtGain(voices, gain, @"level-voice", start, end, cut, scheduler);
                }

                // What the level carries is compressed; the renders stay WAV in the work folder.
                levelAudio = await ToPlayFileAsync(levelAudio);
                if (voiceFile != null)
                    voiceFile = await ToPlayFileAsync(voiceFile);

                string? name = Apply(working, beatmaps, music, editor, levelAudio, out string? error, replaceRendered: true,
                    targetName: LevelAudioFileName(working));
                if (name == null)
                    throw new ForgeException($"could not set the level audio: {error}");

                // The voice file follows: added, replaced, or gone with the voice.
                if (!setVoice(working, beatmaps, editor, voiceFile, out string? voiceError))
                    throw new ForgeException($"could not set the level's voice: {voiceError}");

                ForgeTrackSession.LevelAudioSignature = signature;
                ForgeTrackSession.LevelAudioLufs = double.IsFinite(after) ? after : null;
                ForgeTrackSession.Save();
                return name;
            }
            finally
            {
                Rendering = false;
            }
        }

        /// <summary>
        /// Encode a render as the level's play format (Ogg Vorbis) next to it
        /// in the work folder. Runs off the update thread.
        /// </summary>
        public static Task<string> ToPlayFileAsync(string wav) => Task.Run(() =>
        {
            string ogg = Path.ChangeExtension(wav, PLAY_FILE_EXTENSION);
            var err = new byte[512];
            KanForgeStatus status;

            try
            {
                status = KanForgeNative.EncodeOgg(wav, ogg, PLAY_FILE_QUALITY, err, err.Length);
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new ForgeException("the built-in audio encoder is not available: update kan!");
            }

            if (status != KanForgeStatus.Ok)
                throw new ForgeException($"could not compress the level audio: {System.Text.Encoding.UTF8.GetString(err).TrimEnd('\0')}");

            return ogg;
        });

        /// <summary>Mix, cut to the level audio window, and bring to the game's loudness. Returns the file and the loudness before and after.</summary>
        private static async Task<(string Path, double Before, double After)> renderLevelled(List<ForgeTrack> tracks, double loudnessOffsetDb, double start, double end, bool cut, Scheduler scheduler)
        {
            string mix = await ForgeJobs.RenderMixAsync(tracks, ForgeTrackSession.NewWorkPath("level-audio-mix"), null, scheduler);
            mix = await cutToWindow(mix, @"level-audio", start, end, cut, scheduler);

            // Every level at the game's reference loudness (or the creator's offset from it).
            string levelled = ForgeTrackSession.NewWorkPath("level-audio");
            var (before, after) = await ForgeJobs.NormalizeLoudnessAsync(mix, LevelLoudness.TargetLufs(loudnessOffsetDb), levelled, null, scheduler);
            return (levelled, before, after);
        }

        /// <summary>Mix some of the tracks at a fixed extra gain, cut to the level audio window.</summary>
        private static async Task<string> renderAtGain(List<ForgeTrack> tracks, double gain, string prefix, double start, double end, bool cut, Scheduler scheduler)
        {
            var scaled = tracks.Select(t =>
            {
                var copy = t.Clone();
                copy.Gain = (float)(t.Gain * gain);
                return copy;
            }).ToList();

            string mix = await ForgeJobs.RenderMixAsync(scaled, ForgeTrackSession.NewWorkPath(prefix + "-mix"), null, scheduler);
            return await cutToWindow(mix, prefix, start, end, cut, scheduler);
        }

        private static async Task<string> cutToWindow(string mix, string prefix, double start, double end, bool cut, Scheduler scheduler)
        {
            if (!cut) return mix;

            return await ForgeJobs.EditTrackAsync(mix, KanForgeEditMode.Trim, (float)start,
                end > 0 ? (float)end : ForgeTrack.TO_THE_END_MS,
                ForgeTrackSession.NewWorkPath(prefix), null, scheduler);
        }

        /// <summary>
        /// Put the voice file into the level (null: the level has no voice),
        /// removing any earlier one (the title, and so the name, may have
        /// changed). Returns false with an error.
        /// </summary>
        private static bool setVoice(WorkingBeatmap working, BeatmapManager beatmaps, osu.Game.Screens.Edit.Editor? editor, string? voiceFile, out string? error)
        {
            error = null;

            try
            {
                var set = working.BeatmapSetInfo;
                string? name = voiceFile == null ? null : LevelVoiceFileName(working);

                foreach (var old in set.Files.Where(f => IsLevelVoice(f.Filename) && !SameFile(f.Filename, name)).ToList())
                    beatmaps.DeleteFile(set, old);

                if (voiceFile != null)
                {
                    using var stream = File.OpenRead(voiceFile);
                    beatmaps.AddFile(set, stream, name!);
                }

                SaveEditor(editor);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
