// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Kan.Forge.Media;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// One source's sound drawn behind the notes: the
    /// companion and the reference each get a layer in their role colour,
    /// blended additively so overlaps stay readable, and they follow the
    /// listen toggles. Lives in the piano roll's grid (time × pitch).
    /// </summary>
    internal partial class SpectrogramLayer : CompositeDrawable
    {
        private readonly PianoRoll roll;
        private readonly Color4 colour;

        /// <summary>Where the source starts on the level audio's timeline (ms).</summary>
        public double OffsetMs { get; set; }

        /// <summary>What is drawn ("" while nothing is loaded): changing it recomputes.</summary>
        public string? SourceKey { get; private set; }

        private Sprite? sprite;
        private double lengthMs;
        private int loadGeneration;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        public SpectrogramLayer(PianoRoll roll, Color4 colour)
        {
            this.roll = roll;
            this.colour = colour;
            AlwaysPresent = true;
        }

        /// <summary>Show the spectrogram of <paramref name="audio"/> (decoded and analysed in the background).</summary>
        public void Load(string key, Func<byte[]?> audio, double offsetMs)
        {
            OffsetMs = offsetMs;
            if (key == SourceKey) return;

            SourceKey = key;
            int generation = ++loadGeneration;

            Task.Run<(PitchSpectrogram spectrogram, Image<Rgba32> image)?>(() =>
            {
                byte[]? data = audio();
                if (data == null) return null;

                var mono = PitchSpectrogram.DecodeMono(data, out int rate);
                if (mono == null || rate <= 0) return null;

                var spectrogram = PitchSpectrogram.Compute(mono, rate);
                return (spectrogram, image: toImage(spectrogram, renderer.MaxTextureSize));
            }).ContinueWith(t => Schedule(() =>
            {
                if (generation != loadGeneration || t.IsFaulted || t.Result is not { } r) return;

                var texture = renderer.CreateTexture(r.image.Width, r.image.Height, true, TextureFilteringMode.Linear);
                texture.SetData(new TextureUpload(r.image));

                ClearInternal();
                AddInternal(sprite = new Sprite
                {
                    Texture = texture,
                    Colour = colour,
                    Blending = BlendingParameters.Additive,
                });
                lengthMs = r.spectrogram.LengthMs;
            }));
        }

        public void Clear()
        {
            SourceKey = null;
            loadGeneration++;
            ClearInternal();
            sprite = null;
        }

        protected override void Update()
        {
            base.Update();

            if (sprite == null) return;

            float x0 = roll.XOf(OffsetMs), x1 = roll.XOf(OffsetMs + lengthMs);
            sprite.X = x0;
            sprite.Width = Math.Max(1, x1 - x0);
            sprite.Y = roll.YOf(PitchSpectrogram.PITCHES - 1);
            sprite.Height = PitchSpectrogram.PITCHES * roll.RowHeight;
        }

        /// <summary>White pixels whose alpha is the level (the sprite tints them); top row = highest pitch.</summary>
        private static Image<Rgba32> toImage(PitchSpectrogram s, int maxWidth)
        {
            int width = Math.Max(1, Math.Min(s.Frames, Math.Max(1, maxWidth)));
            var image = new Image<Rgba32>(width, PitchSpectrogram.PITCHES);
            double framesPerColumn = (double)s.Frames / width;

            for (int x = 0; x < width; x++)
            {
                int f0 = (int)(x * framesPerColumn), f1 = Math.Max(f0 + 1, (int)((x + 1) * framesPerColumn));

                for (int midi = 0; midi < PitchSpectrogram.PITCHES; midi++)
                {
                    float v = 0;
                    for (int f = f0; f < f1 && f < s.Frames; f++)
                        v = Math.Max(v, s.At(f, midi));

                    // A gentle curve: quiet partials fade, strong notes stand out.
                    byte a = (byte)(Math.Clamp(v * v, 0, 1) * 200);
                    image[x, PitchSpectrogram.PITCHES - 1 - midi] = new Rgba32(255, 255, 255, a);
                }
            }

            return image;
        }
    }
}
