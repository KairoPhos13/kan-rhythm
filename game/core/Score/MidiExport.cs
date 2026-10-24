// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kan.Core.Score
{
    /// <summary>
    /// Standard MIDI File (format 0) writer for validation exports (owner
    /// 2026-10-14): a corrected score saved next to its audio is ground
    /// truth for measuring the transcriber later (tools/dataset_bench reads
    /// these files). Times are kept exact in milliseconds through a single
    /// tempo; velocity carries the intensity. Cents are not written.
    /// </summary>
    public static class MidiExport
    {
        /// <summary>Ticks per quarter note.</summary>
        public const int PPQ = 960;

        /// <summary>One note to write: start and length in ms, MIDI pitch, velocity 1..127.</summary>
        public readonly record struct Note(double StartMs, double DurationMs, int Midi, int Velocity);

        /// <summary>Velocity for an intensity of 0..1 (never 0: that would be a note-off).</summary>
        public static int Velocity(float intensity) => Math.Clamp((int)Math.Round(1 + Math.Clamp(intensity, 0, 1) * 126), 1, 127);

        /// <param name="notes">Notes with non-negative start times.</param>
        /// <param name="beatLengthMs">The tempo written into the file (any tempo keeps the times exact).</param>
        /// <param name="beatsPerBar">The time signature's top number.</param>
        public static byte[] Write(IEnumerable<Note> notes, string trackName, double beatLengthMs = 500, int beatsPerBar = 4)
        {
            // The tempo as written (whole microseconds); ticks follow it exactly.
            int tempo = (int)Math.Round(Math.Clamp(beatLengthMs, 6, 60000) * 1000);
            double ticksPerMs = PPQ * 1000.0 / tempo;

            // (tick, order, bytes): note-offs before note-ons on the same tick, so a
            // repeated pitch ends before it starts again.
            var events = new List<(long tick, int order, byte[] data)>();

            foreach (var n in notes)
            {
                if (n.Midi < 0 || n.Midi > 127 || n.StartMs < 0) continue;

                long on = (long)Math.Round(n.StartMs * ticksPerMs);
                long off = Math.Max(on + 1, (long)Math.Round((n.StartMs + Math.Max(0, n.DurationMs)) * ticksPerMs));
                byte velocity = (byte)Math.Clamp(n.Velocity, 1, 127);

                events.Add((on, 1, new byte[] { 0x90, (byte)n.Midi, velocity }));
                events.Add((off, 0, new byte[] { 0x80, (byte)n.Midi, 0 }));
            }

            var track = new List<byte>();

            // Track name, tempo, time signature (denominator 4, 24 clocks, 8 32nds).
            byte[] name = Encoding.UTF8.GetBytes(trackName);
            meta(track, 0x03, name);
            meta(track, 0x51, new[] { (byte)(tempo >> 16), (byte)(tempo >> 8), (byte)tempo });
            meta(track, 0x58, new byte[] { (byte)Math.Clamp(beatsPerBar, 1, 32), 2, 24, 8 });

            long last = 0;

            foreach (var (tick, _, data) in events.OrderBy(e => e.tick).ThenBy(e => e.order))
            {
                vlq(track, (uint)(tick - last));
                track.AddRange(data);
                last = tick;
            }

            vlq(track, 0);
            track.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });

            var file = new List<byte>();
            file.AddRange(Encoding.ASCII.GetBytes("MThd"));
            u32(file, 6);
            file.AddRange(new byte[] { 0, 0, 0, 1, PPQ >> 8, PPQ & 0xFF });
            file.AddRange(Encoding.ASCII.GetBytes("MTrk"));
            u32(file, (uint)track.Count);
            file.AddRange(track);
            return file.ToArray();
        }

        public static void WriteFile(string path, IEnumerable<Note> notes, string trackName, double beatLengthMs = 500, int beatsPerBar = 4)
            => File.WriteAllBytes(path, Write(notes, trackName, beatLengthMs, beatsPerBar));

        private static void meta(List<byte> track, byte type, byte[] data)
        {
            vlq(track, 0);
            track.Add(0xFF);
            track.Add(type);
            vlq(track, (uint)data.Length);
            track.AddRange(data);
        }

        private static void u32(List<byte> o, uint v)
        {
            o.Add((byte)(v >> 24));
            o.Add((byte)(v >> 16));
            o.Add((byte)(v >> 8));
            o.Add((byte)v);
        }

        private static void vlq(List<byte> o, uint v)
        {
            Span<byte> buf = stackalloc byte[5];
            int n = 0;
            buf[n++] = (byte)(v & 0x7F);
            while ((v >>= 7) != 0) buf[n++] = (byte)(0x80 | (v & 0x7F));
            while (n-- > 0) o.Add(buf[n]);
        }
    }
}
