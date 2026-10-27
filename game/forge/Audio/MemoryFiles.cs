// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace Kan.Forge.Audio
{
    /// <summary>
    /// Sounds made in memory (a synthesised tone, a chime), served by name to
    /// an osu! sample store: add a file before asking the store for it.
    /// </summary>
    public sealed class MemoryFiles : IResourceStore<byte[]>
    {
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();

        public void Add(string name, byte[] data) => files[name] = data;

        public bool Contains(string name) => files.ContainsKey(name);

        public byte[] Get(string name) => files.TryGetValue(name, out var data) ? data : null!;

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public Stream GetStream(string name) => files.TryGetValue(name, out var data) ? new MemoryStream(data, false) : null!;

        public IEnumerable<string> GetAvailableResources() => files.Keys;

        public void Dispose()
        {
        }
    }
}
