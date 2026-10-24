// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Score;

namespace Kan.Core.Tests
{
    /// <summary>MIDI export of a corrected score (validation ground truth, owner 2026-10-14).</summary>
    [TestFixture]
    public class MidiExportTest
    {
        [TestCase(500.0)]
        [TestCase(60000 / 97.3)]
        public void TimesSurviveTheRoundTrip(double beatLengthMs)
        {
            var notes = new[]
            {
                new MidiExport.Note(0, 250, 72, 100),
                new MidiExport.Note(250, 250, 72, 64), // same pitch again right after: off before on
                new MidiExport.Note(1234.5, 87.25, 76, 1),
                new MidiExport.Note(1300, 900, 60, 127), // overlaps the previous note
            };

            var back = read(MidiExport.Write(notes, "test", beatLengthMs, 3));

            Assert.That(back.Count, Is.EqualTo(notes.Length));

            var expected = notes.OrderBy(n => n.StartMs).ThenBy(n => n.Midi).ToList();
            var actual = back.OrderBy(n => n.StartMs).ThenBy(n => n.Midi).ToList();

            // One tick is at most beatLength / 960 ms.
            double tolerance = beatLengthMs / MidiExport.PPQ;

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].Midi, Is.EqualTo(expected[i].Midi));
                Assert.That(actual[i].Velocity, Is.EqualTo(expected[i].Velocity));
                Assert.That(actual[i].StartMs, Is.EqualTo(expected[i].StartMs).Within(tolerance));
                Assert.That(actual[i].DurationMs, Is.EqualTo(expected[i].DurationMs).Within(tolerance * 2));
            }
        }

        [Test]
        public void SkipsNotesItCannotWrite()
        {
            var back = read(MidiExport.Write(new[]
            {
                new MidiExport.Note(-10, 100, 60, 80), // before the file starts
                new MidiExport.Note(10, 100, -1, 80), // no pitch
                new MidiExport.Note(20, 100, 61, 80),
            }, "x"));

            Assert.That(back.Single().Midi, Is.EqualTo(61));
        }

        [Test]
        public void IntensityMapsToAudibleVelocity()
        {
            Assert.That(MidiExport.Velocity(0), Is.EqualTo(1), "velocity 0 would be read as a note-off");
            Assert.That(MidiExport.Velocity(1), Is.EqualTo(127));
            Assert.That(MidiExport.Velocity(0.5f), Is.EqualTo(64));
        }

        /// <summary>A minimal format-0 reader with a tempo map (like tools/dataset_bench's).</summary>
        private static List<MidiExport.Note> read(byte[] d)
        {
            Assert.That(System.Text.Encoding.ASCII.GetString(d, 0, 4), Is.EqualTo("MThd"));
            int ppq = (d[12] << 8) | d[13];
            Assert.That(System.Text.Encoding.ASCII.GetString(d, 14, 4), Is.EqualTo("MTrk"));
            int end = 22 + ((d[18] << 24) | (d[19] << 16) | (d[20] << 8) | d[21]);
            Assert.That(end, Is.EqualTo(d.Length));

            int p = 22;
            long tick = 0;
            double ms = 0, msPerTick = 500.0 / ppq;
            var open = new Dictionary<int, Queue<(double ms, int vel)>>();
            var notes = new List<MidiExport.Note>();

            while (p < end)
            {
                long delta = 0;
                byte b;
                do
                {
                    b = d[p++];
                    delta = (delta << 7) | (uint)(b & 0x7F);
                } while ((b & 0x80) != 0);

                tick += delta;
                ms += delta * msPerTick;

                byte status = d[p++];

                if (status == 0xFF)
                {
                    byte type = d[p++];
                    int len = d[p++];
                    if (type == 0x51) msPerTick = ((d[p] << 16) | (d[p + 1] << 8) | d[p + 2]) / 1000.0 / ppq;
                    if (type == 0x2F) break;
                    p += len;
                    continue;
                }

                int key = d[p++], vel = d[p++];

                if ((status & 0xF0) == 0x90 && vel > 0)
                {
                    if (!open.TryGetValue(key, out var q)) open[key] = q = new Queue<(double, int)>();
                    q.Enqueue((ms, vel));
                }
                else
                {
                    var (start, v) = open[key].Dequeue();
                    notes.Add(new MidiExport.Note(start, ms - start, key, v));
                }
            }

            Assert.That(open.Values.All(q => q.Count == 0), "every note is closed");
            return notes;
        }
    }
}
