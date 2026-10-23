// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Logging;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// A play, note by note, for judging the judge (owner 2026-10-21: "the
    /// pitch trail lands on the note on time, yet it waits too long for the
    /// perfect, or gives a meh"). One CSV per play in the logs folder
    /// (<c>kan-play-*.csv</c>, the last <see cref="kept"/> kept): every heard
    /// onset with its stream time, where it was placed on the level and when
    /// it arrived; every pitch fact and pitch reading; every verdict with the
    /// onset it took; slow frames; the level clock against real time. Lines
    /// are gathered on the update thread and written in the background.
    /// </summary>
    public sealed class KanPlayTrace : IDisposable
    {
        private const int kept = 20;

        private readonly string path;
        private readonly object gate = new object();
        private StringBuilder pending = new StringBuilder(64 * 1024);
        private Task writing = Task.CompletedTask;
        private long lastFlush = Environment.TickCount64;

        private KanPlayTrace(string path) => this.path = path;

        /// <summary>A trace for a new play, or null when the logs folder is not known.</summary>
        public static KanPlayTrace? Start(IEnumerable<string> header)
        {
            string? dir = Logger.Storage?.GetFullPath(string.Empty);
            if (string.IsNullOrEmpty(dir)) return null;

            try
            {
                foreach (var old in Directory.GetFiles(dir, "kan-play-*.csv").OrderByDescending(f => f).Skip(kept - 1))
                    File.Delete(old);
            }
            catch (IOException)
            {
            }

            var trace = new KanPlayTrace(Path.Combine(dir, $"kan-play-{DateTime.Now:yyyyMMdd-HHmmss}.csv"));

            foreach (string line in header)
                trace.pending.Append("# ").Append(line).Append('\n');

            trace.pending.Append("real_ms,level_ms,kind,a,b,c,d,e,f,g,h\n");
            return trace;
        }

        /// <summary>One row: when (real ms since listening began, level ms), what, and its values.</summary>
        public void Row(double realMs, double levelMs, string kind, params object?[] values)
        {
            var sb = pending;
            sb.Append(realMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(levelMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(kind);

            foreach (object? v in values)
            {
                sb.Append(',');
                sb.Append(v switch
                {
                    null => string.Empty,
                    double d => d.ToString("0.0", CultureInfo.InvariantCulture),
                    float f => f.ToString("0.00", CultureInfo.InvariantCulture),
                    IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
                    _ => v.ToString(),
                });
            }

            sb.Append('\n');

            if (Environment.TickCount64 - lastFlush > 2000)
                flush();
        }

        private void flush()
        {
            lastFlush = Environment.TickCount64;
            if (pending.Length == 0) return;

            string text = pending.ToString();
            pending.Clear();

            lock (gate)
            {
                writing = writing.ContinueWith(_ =>
                {
                    try
                    {
                        File.AppendAllText(path, text);
                    }
                    catch (IOException)
                    {
                    }
                }, TaskScheduler.Default);
            }
        }

        public void Dispose()
        {
            flush();
            Logger.Log($"kan!: play trace written to {path}");
        }
    }
}
