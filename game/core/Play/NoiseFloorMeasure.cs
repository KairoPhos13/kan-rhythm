// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Play
{
    /// <summary>
    /// How loud the room is when nobody plays: the noise-floor calibration of
    /// issue 17's second step. The readings are the live pipeline's own
    /// per-10-ms RMS levels (what a play listens to), so the floor measured
    /// here is the one the note detector works against. The floor is their
    /// median (a door or a breath in the middle of the listening must not
    /// raise it), and the gate opens <see cref="OPEN_DB"/> above it, as
    /// libkan's adaptive noise gate does (libkan/include/libkan/precondition.hpp).
    /// </summary>
    public sealed class NoiseFloorMeasure
    {
        /// <summary>How far above the room's level a sound must be to be heard as a note (dB), as libkan's noise gate.</summary>
        public const double OPEN_DB = 9;

        /// <summary>Levels kept (10 ms apart: 30 s of room).</summary>
        public const int MAX_READINGS = 3000;

        private readonly double[] levels = new double[MAX_READINGS];
        private int count;

        /// <summary>One level reading (RMS, 0..1) from the live pipeline.</summary>
        public void Add(double levelRms)
        {
            if (levelRms <= 0 || count >= MAX_READINGS) return;
            levels[count++] = levelRms;
        }

        /// <summary>Level readings collected so far.</summary>
        public int Readings => count;

        /// <summary>What the room sounds like when nobody plays.</summary>
        public NoiseFloorResult Finish()
        {
            if (count == 0) return new NoiseFloorResult(false, 0, 0, 0, 0, 0, 0);

            var sorted = (double[])levels.Clone();
            Array.Sort(sorted, 0, count);

            double floor = count % 2 == 1 ? sorted[count / 2] : (sorted[count / 2 - 1] + sorted[count / 2]) / 2;
            double peak = sorted[count - 1];

            return new NoiseFloorResult(true, floor, floor * Math.Pow(10, OPEN_DB / 20), peak, Db(floor), floor * floor, count);
        }

        /// <summary>A level (RMS) in decibels, as a meter shows it.</summary>
        public static double Db(double rms) => rms <= 0 ? double.NegativeInfinity : 20 * Math.Log10(rms);
    }

    /// <summary>One noise-floor measurement (see <see cref="NoiseFloorMeasure"/>).</summary>
    /// <param name="Valid">Something was heard (silence is also something).</param>
    /// <param name="FloorRms">The room's level while nobody plays (RMS).</param>
    /// <param name="OpenRms">The level at which a sound counts as a note (floor + <see cref="NoiseFloorMeasure.OPEN_DB"/> dB).</param>
    /// <param name="PeakRms">The loudest moment heard (a door, a breath: questioned gently, never blamed).</param>
    /// <param name="FloorDb">The floor in decibels, for showing what was heard.</param>
    /// <param name="Energy">The floor as mean-squared amplitude, the units the note detector's floor takes.</param>
    /// <param name="Readings">How many level readings the measurement used.</param>
    public readonly record struct NoiseFloorResult(bool Valid, double FloorRms, double OpenRms, double PeakRms, double FloorDb, double Energy, int Readings);
}
