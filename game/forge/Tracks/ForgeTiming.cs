// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Bindables;

namespace Kan.Forge.Tracks
{
    /// <summary>
    /// The player's timing corrections, shared by every
    /// level and kept for gameplay, which will judge microphone input with
    /// the same numbers:
    /// <list type="bullet">
    /// <item><b>Mic delay</b>, the full round trip of playing along: the
    /// speakers' delay, the microphone's, and the transcriber's reaction.
    /// Measured with the calibration tool; applied to notes recorded with the
    /// mic.</item>
    /// <item><b>Transcriber delay</b>, how late the transcriber places notes
    /// in an audio file (slow attacks are recognised after they start);
    /// applied to every note transcribed from a file.</item>
    /// <item><b>Player timing</b>, how far ahead of (or behind) the line the
    /// player plays by habit; applied to every note they play live.</item>
    /// </list>
    /// Stored in kan-forge/timing.json.
    /// </summary>
    public static class ForgeTiming
    {
        /// <summary>
        /// Never below 0: microphone, driver and processing
        /// can only make a note heard later. A measurement below 0 is the player
        /// playing ahead, not the device; a saved value below 0 loads as 0.
        /// </summary>
        public static readonly BindableDouble MIC_DELAY_MS = new BindableDouble { MinValue = 0, MaxValue = 600, Precision = 1 };

        /// <summary>
        /// The player's habit, in ms: negative when they play ahead of the line
        /// (owner 2026-10-21: a wind player sounds the attack before hearing the
        /// note, ~45 ms ahead every time). Their notes are taken this much later
        /// (earlier when positive), the player's own offset, as osu!'s, and the
        /// one a play corrects; unlike the mic delay, it may be negative.
        /// </summary>
        public static readonly BindableDouble PLAYER_TIMING_MS = new BindableDouble { MinValue = -200, MaxValue = 200, Precision = 1 };

        public static readonly BindableDouble TRANSCRIBER_DELAY_MS = new BindableDouble { MinValue = -200, MaxValue = 300, Precision = 1 };

        /// <summary>
        /// How far the player's instrument sits from A440, in cents (owner
        /// 2026-10-15: "my instrument is almost a semitone sharp on every
        /// note"). Taken off everything the game hears before judging.
        /// </summary>
        public static readonly BindableDouble TUNING_CENTS = new BindableDouble { MinValue = -80, MaxValue = 80, Precision = 1 };

        /// <summary>Learn <see cref="TUNING_CENTS"/> while playing (<see cref="Kan.Core.Play.TuningTracker"/>).</summary>
        public static readonly BindableBool AUTO_TUNING = new BindableBool(true);

        /// <summary>When the mic delay was last measured, and with which instrument (empty = never).</summary>
        public static DateTime MicCalibratedAtUtc { get; private set; }

        public static string MicCalibratedInstrument { get; private set; } = string.Empty;

        /// <summary>
        /// How loud the room is when nobody plays (RMS, issue 17's noise-floor
        /// step): the note detector's floor is built from it, so a humming room
        /// stops raising false notes. 0 = never measured (the detector keeps
        /// its own default).
        /// </summary>
        public static double NoiseFloorRms { get; private set; }

        /// <summary>When the room was last listened to, and with which instrument's microphone (empty = never).</summary>
        public static DateTime NoiseCalibratedAtUtc { get; private set; }

        public static string NoiseCalibratedInstrument { get; private set; } = string.Empty;

        /// <summary>A calibration's result was kept (the settings text follows it).</summary>
        public static event Action? Calibrated;

        private static string? path;
        private static bool loading;

        private class Document
        {
            public double MicDelayMs { get; set; }

            public DateTime MicCalibratedAtUtc { get; set; }

            public string MicCalibratedInstrument { get; set; } = string.Empty;

            public double TranscriberDelayMs { get; set; }

            public double PlayerTimingMs { get; set; }

            public double TuningCents { get; set; }

            public bool AutoTuning { get; set; } = true;

            public double NoiseFloorRms { get; set; }

            public DateTime NoiseCalibratedAtUtc { get; set; }

            public string NoiseCalibratedInstrument { get; set; } = string.Empty;
        }

        static ForgeTiming()
        {
            MIC_DELAY_MS.BindValueChanged(_ => save());
            TRANSCRIBER_DELAY_MS.BindValueChanged(_ => save());
            PLAYER_TIMING_MS.BindValueChanged(_ => save());
            TUNING_CENTS.BindValueChanged(_ => save());
            AUTO_TUNING.BindValueChanged(_ => save());
        }

        /// <summary>Read the saved values (once per storage folder).</summary>
        public static void Load(string forgeRoot)
        {
            string file = Path.Combine(forgeRoot, @"timing.json");
            if (file == path) return;

            path = file;
            loading = true;

            try
            {
                var doc = File.Exists(file) ? JsonSerializer.Deserialize<Document>(File.ReadAllText(file)) : null;
                MIC_DELAY_MS.Value = doc?.MicDelayMs ?? 0;
                TRANSCRIBER_DELAY_MS.Value = doc?.TranscriberDelayMs ?? 0;
                PLAYER_TIMING_MS.Value = doc?.PlayerTimingMs ?? 0;
                TUNING_CENTS.Value = doc?.TuningCents ?? 0;
                AUTO_TUNING.Value = doc?.AutoTuning ?? true;
                MicCalibratedAtUtc = doc?.MicCalibratedAtUtc ?? default;
                MicCalibratedInstrument = doc?.MicCalibratedInstrument ?? string.Empty;
                NoiseFloorRms = doc?.NoiseFloorRms ?? 0;
                NoiseCalibratedAtUtc = doc?.NoiseCalibratedAtUtc ?? default;
                NoiseCalibratedInstrument = doc?.NoiseCalibratedInstrument ?? string.Empty;
            }
            catch (Exception)
            {
                // A damaged file: start from zero (it is rewritten on the next change).
            }
            finally
            {
                loading = false;
            }
        }

        /// <summary>Keep a calibration's result.</summary>
        public static void SetCalibratedMicDelay(double ms, string instrument)
        {
            MicCalibratedAtUtc = DateTime.UtcNow;
            MicCalibratedInstrument = instrument;
            MIC_DELAY_MS.Value = Math.Round(ms);
            save();
            Calibrated?.Invoke();
        }

        /// <summary>Keep the noise floor the room's listening measured (issue 17).</summary>
        public static void SetCalibratedNoiseFloor(double rms, string instrument)
        {
            NoiseCalibratedAtUtc = DateTime.UtcNow;
            NoiseCalibratedInstrument = instrument;
            NoiseFloorRms = rms;
            save();
            Calibrated?.Invoke();
        }

        private static void save()
        {
            if (loading || path == null) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + @".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new Document
                {
                    MicDelayMs = MIC_DELAY_MS.Value,
                    MicCalibratedAtUtc = MicCalibratedAtUtc,
                    MicCalibratedInstrument = MicCalibratedInstrument,
                    TranscriberDelayMs = TRANSCRIBER_DELAY_MS.Value,
                    PlayerTimingMs = PLAYER_TIMING_MS.Value,
                    TuningCents = TUNING_CENTS.Value,
                    AutoTuning = AUTO_TUNING.Value,
                    NoiseFloorRms = NoiseFloorRms,
                    NoiseCalibratedAtUtc = NoiseCalibratedAtUtc,
                    NoiseCalibratedInstrument = NoiseCalibratedInstrument,
                }, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, path, true);
            }
            catch (Exception)
            {
                // Not fatal: the values still apply for this session.
            }
        }
    }

    /// <summary>
    /// Mic delay from a calibration take: clicks the player played along to,
    /// and the note starts heard in the recording (both on the same clock).
    /// </summary>
    public static class MicCalibration
    {
        /// <summary>A note this far before its click is still taken as an answer to it (anticipation).</summary>
        public const double EARLY_MS = 120;

        public readonly record struct Result(double DelayMs, double SpreadMs, int Matched, int Clicks)
        {
            /// <summary>Enough answers, consistent enough to trust.</summary>
            public bool Reliable => Matched >= Math.Max(4, Clicks * 2 / 3) && SpreadMs <= 40;
        }

        /// <summary>
        /// For each click, the first note start from <see cref="EARLY_MS"/>
        /// before it up to most of the way to the next click; the delay is the
        /// median lateness, the spread its median absolute deviation (a few
        /// missed or doubled notes don't move it).
        /// </summary>
        public static Result Estimate(IReadOnlyList<double> clicksMs, IReadOnlyList<double> onsetsMs)
        {
            var onsets = onsetsMs.OrderBy(o => o).ToList();
            var lateness = new List<double>();

            for (int i = 0; i < clicksMs.Count; i++)
            {
                double click = clicksMs[i];
                double interval = i + 1 < clicksMs.Count ? clicksMs[i + 1] - click : (i > 0 ? click - clicksMs[i - 1] : 1000);
                double from = click - EARLY_MS, to = click + interval * 0.75;

                double? first = onsets.Cast<double?>().FirstOrDefault(o => o >= from && o < to);
                if (first != null) lateness.Add(first.Value - click);
            }

            if (lateness.Count == 0) return new Result(0, double.PositiveInfinity, 0, clicksMs.Count);

            double median = Median(lateness);
            double spread = Median(lateness.Select(l => Math.Abs(l - median)).ToList());
            return new Result(median, spread, lateness.Count, clicksMs.Count);
        }

        public static double Median(IReadOnlyList<double> values)
        {
            var s = values.OrderBy(v => v).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
        }
    }
}
