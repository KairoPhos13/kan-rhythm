// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using System.Threading.Tasks;
using Kan.Forge.Native;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;

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
    internal static class BeatmapAudio
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

                    for (int i = 1; set.Files.Any(f => f.Filename == name); i++)
                        name = Path.GetFileNameWithoutExtension(name) + $"-{i}" + Path.GetExtension(name);
                }

                using (var stream = File.OpenRead(sourcePath))
                    beatmaps.AddFile(set, stream, name);

                string previous = working.BeatmapInfo.Metadata.AudioFile;
                working.BeatmapInfo.Metadata.AudioFile = name;

                // A new render replaces the previous one (renders are ours to
                // remove; imported audio files are never deleted).
                if (replaceRendered && isRender(previous) && previous != name && set.GetFile(previous) is { } old)
                    beatmaps.DeleteFile(set, old);

                SaveEditor(editor);
                music.ReloadCurrentTrack();
                return name;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>The level audio's file name: named after the level ("My Song - level audio.wav").</summary>
        public static string LevelAudioFileName(WorkingBeatmap working)
        {
            string title = working.BeatmapInfo.Metadata.Title?.Trim() ?? string.Empty;
            string safe = new string(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '/' && c != '\\').ToArray()).Trim();

            return string.IsNullOrEmpty(safe) ? LEVEL_AUDIO_SUFFIX.TrimStart(' ', '-') : safe + LEVEL_AUDIO_SUFFIX;
        }

        private const string LEVEL_AUDIO_SUFFIX = @" - level audio.wav";

        private static bool isRender(string? fileName)
            => !string.IsNullOrEmpty(fileName)
               && (fileName.EndsWith(LEVEL_AUDIO_SUFFIX.TrimStart(' ', '-'), StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(@"level-audio", StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(@"audio-render", StringComparison.OrdinalIgnoreCase)
                   || fileName.StartsWith(@"render-", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The stock flow saves after a resource change, via the editor's
        /// internal <c>Save()</c>. Plugin code reaches it by a guarded
        /// reflection call (same pattern as hiding the stock Audio Track
        /// row): if an upstream rename breaks it, the save is skipped — the
        /// track still switches — and the user's normal save persists it.
        /// </summary>
        public static void SaveEditor(osu.Game.Screens.Edit.Editor? editor)
        {
            if (editor == null) return;

            try
            {
                typeof(osu.Game.Screens.Edit.Editor)
                    .GetMethod("Save", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)
                    ?.Invoke(editor, null);
            }
            catch
            {
                // Never break the connection over persistence.
            }
        }

        // ------------------------------------------------------------------
        // The level audio = the companion mix (owner naming 2026-10-14)
        // ------------------------------------------------------------------

        /// <summary>Everything the level audio is made of, as one comparable string.</summary>
        public static string Signature(ForgeTrackSet set)
        {
            var parts = set.Companions.Select(t =>
                $"{t.Path}|{t.Gain:0.000}|{t.OffsetMs:0}|{t.ClipStartMs:0}-{t.ClipEndMs:0}|{t.Edits.LowCutHz:0}/{t.Edits.HighCutHz:0}|"
                + string.Join(';', t.Edits.Ranges.Select(r => $"{(int)r.Mode}:{r.FromMs:0}-{r.ToMs:0}")));

            return $"{set.AudioStartMs:0}-{set.AudioEndMs:0}#" + string.Join('#', parts);
        }

        /// <summary>The level audio matches the companion mix as it is now.</summary>
        public static bool IsUpToDate(ForgeTrackSet set) => ForgeTrackSession.LevelAudioSignature == Signature(set);

        /// <summary>A render is running (one at a time).</summary>
        public static bool Rendering { get; private set; }

        /// <summary>
        /// Render the companion tracks — as heard: edits, filters, trims,
        /// positions — cut to the level audio window, and install the result
        /// as the level's audio. Call from the update thread; returns the
        /// beatmap file name.
        /// </summary>
        public static async Task<string> RenderAsync(ForgeTrackSet set, WorkingBeatmap working, BeatmapManager beatmaps,
                                                     MusicController music, osu.Game.Screens.Edit.Editor? editor, Scheduler scheduler)
        {
            var companions = set.Companions.ToList();
            if (companions.Count == 0)
                throw new ForgeException("no companion tracks — mark at least one track as companion");

            if (Rendering)
                throw new ForgeException("the level audio is already being rendered");

            string signature = Signature(set);
            double start = set.AudioStartMs, end = set.AudioEndMs;
            bool cut = set.HasAudioWindow;

            Rendering = true;

            try
            {
                string mix = await ForgeJobs.RenderMixAsync(companions, ForgeTrackSession.NewWorkPath("level-audio-mix"), null, scheduler);

                if (cut)
                {
                    mix = await ForgeJobs.EditTrackAsync(mix, KanForgeEditMode.Trim, (float)start,
                        end > 0 ? (float)end : ForgeTrack.TO_THE_END_MS,
                        ForgeTrackSession.NewWorkPath("level-audio"), null, scheduler);
                }

                string? name = Apply(working, beatmaps, music, editor, mix, out string? error, replaceRendered: true,
                    targetName: LevelAudioFileName(working));
                if (name == null)
                    throw new ForgeException($"could not set the level audio: {error}");

                ForgeTrackSession.LevelAudioSignature = signature;
                ForgeTrackSession.Save();
                return name;
            }
            finally
            {
                Rendering = false;
            }
        }
    }
}
