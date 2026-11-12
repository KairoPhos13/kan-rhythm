// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Design
{
    /// <summary>How a colour is seen: typical vision, or one of the three deficiencies kan! has safe palettes for.</summary>
    public enum KanVision
    {
        /// <summary>Typical colour vision (no simulation).</summary>
        Typical,

        /// <summary>Green cones missing or shifted (about 6% of men).</summary>
        Deuteranopia,

        /// <summary>Red cones missing or shifted (about 2% of men).</summary>
        Protanopia,

        /// <summary>Blue cones missing or shifted (rare, under 0.02%).</summary>
        Tritanopia,
    }

    /// <summary>
    /// Colour maths for the accessibility work (issue 18): how colours look
    /// to a colour-blind eye, how far apart two colours read, and how much
    /// contrast they give. Simulation follows Machado, Oliveira &amp;
    /// Fernandes 2009 ("A Physiologically-Based Model for Simulation of
    /// Color Vision Deficiency"), their matrices at severity 1.0 applied to
    /// linear RGB; distances are Euclidean in OKLab (perceptually even), and
    /// contrast is the WCAG 2.1 ratio. Pure functions on hex strings
    /// (RRGGBB, as <see cref="KanPalette"/> stores them), so the editor and
    /// the tests share exactly what the game draws.
    /// </summary>
    public static class KanColourVision
    {
        // Machado et al. 2009, Table I (severity 1.0): linear RGB in, linear RGB out.
        private static readonly double[,] deuteranopia =
        {
            { 0.367322, 0.860646, -0.227968 },
            { 0.280085, 0.672501, 0.047413 },
            { -0.011820, 0.042940, 0.968881 },
        };

        private static readonly double[,] protanopia =
        {
            { 0.152286, 1.052583, -0.204868 },
            { 0.114503, 0.786281, 0.099216 },
            { -0.003882, -0.048116, 1.051998 },
        };

        private static readonly double[,] tritanopia =
        {
            { 1.255528, -0.076749, -0.178779 },
            { -0.078411, 0.930809, 0.147602 },
            { 0.004733, 0.691367, 0.303900 },
        };

        /// <summary>How a colour reads to the given kind of vision (same hex format in and out).</summary>
        public static string Simulate(string hex, KanVision vision)
        {
            double[] rgb = toLinear(parse(hex));
            double[,] m = vision switch
            {
                KanVision.Deuteranopia => deuteranopia,
                KanVision.Protanopia => protanopia,
                KanVision.Tritanopia => tritanopia,
                _ => identity,
            };

            return hex3(toSrgb(multiply(m, rgb)));
        }

        /// <summary>How far apart two colours read (OKLab distance, 0 = same, ~0.02 = just told apart).</summary>
        public static double Distance(string hexA, string hexB, KanVision vision)
        {
            double[] a = oklab(toLinear(parse(Simulate(hexA, vision))));
            double[] b = oklab(toLinear(parse(Simulate(hexB, vision))));
            double dl = a[0] - b[0], da = a[1] - b[1], db = a[2] - b[2];
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        /// <summary>WCAG 2.1 contrast ratio (1 = same colour, 21 = white on black); 3 is the bar for graphics, 4.5 for small text.</summary>
        public static double ContrastRatio(string hexA, string hexB)
        {
            double a = luminance(parse(hexA)), b = luminance(parse(hexB));
            double hi = Math.Max(a, b), lo = Math.Min(a, b);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>The colour in OKLCH (lightness, chroma, hue in degrees), the space the safe palettes are designed in.</summary>
        public static (double L, double C, double H) ToOkLch(string hex)
        {
            double[] lab = oklab(toLinear(parse(hex)));
            double c = Math.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]);
            double h = Math.Atan2(lab[2], lab[1]) * 180 / Math.PI;
            return (lab[0], c, h < 0 ? h + 360 : h);
        }

        /// <summary>
        /// A colour from OKLCH, chroma reduced as far as it must be to fit on
        /// a screen (a pale or vivid colour at the edge of what a display can
        /// show comes out less chromatic, never clipped to a wrong hue).
        /// </summary>
        public static string FromOkLch(double l, double c, double h)
        {
            c = Math.Min(c, MaxChroma(l, h));
            double r = h * Math.PI / 180;
            return hex3(toSrgb(oklabToLinear(new[] { l, c * Math.Cos(r), c * Math.Sin(r) })));
        }

        /// <summary>The most chroma a colour of this lightness and hue can have on screen (for the search above).</summary>
        public static double MaxChroma(double l, double h)
        {
            double lo = 0, hi = 0.5;

            for (int i = 0; i < 24; i++)
            {
                double mid = (lo + hi) / 2;
                if (inGamut(oklabToLinear(new[] { l, mid * Math.Cos(h * Math.PI / 180), mid * Math.Sin(h * Math.PI / 180) })))
                    lo = mid;
                else
                    hi = mid;
            }

            return lo;
        }

        private static readonly double[,] identity = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        private static double[] multiply(double[,] m, double[] v) => new[]
        {
            m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2],
            m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2],
            m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2],
        };

        private static double[] parse(string hex)
        {
            string h = hex.StartsWith(@"#", StringComparison.Ordinal) ? hex[1..] : hex;

            if (h.Length != 6)
                throw new ArgumentException($@"not an RRGGBB colour: {hex}", nameof(hex));

            return new[]
            {
                Convert.ToInt32(h[..2], 16) / 255.0,
                Convert.ToInt32(h[2..4], 16) / 255.0,
                Convert.ToInt32(h[4..6], 16) / 255.0,
            };
        }

        private static string hex3(double[] srgb)
        {
            string one(double v) => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString(@"x2");
            return one(srgb[0]) + one(srgb[1]) + one(srgb[2]);
        }

        private static double[] toLinear(double[] srgb)
        {
            double channel(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            return new[] { channel(srgb[0]), channel(srgb[1]), channel(srgb[2]) };
        }

        private static double[] toSrgb(double[] linear)
        {
            // Out-of-gamut simulation results (a hair below 0 or above 1) clip to the nearest displayable colour.
            double channel(double c) => c <= 0 ? 0 : c >= 1 ? 1 : c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            return new[] { channel(linear[0]), channel(linear[1]), channel(linear[2]) };
        }

        private static double[] oklabToLinear(double[] lab)
        {
            double l = lab[0] + 0.3963377774 * lab[1] + 0.2158037573 * lab[2];
            double m = lab[0] - 0.1055613458 * lab[1] - 0.0638541728 * lab[2];
            double s = lab[0] - 0.0894841775 * lab[1] - 1.2914855480 * lab[2];
            l *= l * l;
            m *= m * m;
            s *= s * s;

            return new[]
            {
                +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s,
            };
        }

        private static bool inGamut(double[] linear)
            => linear[0] >= -0.001 && linear[0] <= 1.001
               && linear[1] >= -0.001 && linear[1] <= 1.001
               && linear[2] >= -0.001 && linear[2] <= 1.001;

        private static double[] oklab(double[] linear)
        {
            double l = 0.4122214708 * linear[0] + 0.5363325363 * linear[1] + 0.0514459929 * linear[2];
            double m = 0.2119034982 * linear[0] + 0.6806995451 * linear[1] + 0.1073969566 * linear[2];
            double s = 0.0883024619 * linear[0] + 0.2817188376 * linear[1] + 0.6299787005 * linear[2];
            l = Math.Cbrt(l);
            m = Math.Cbrt(m);
            s = Math.Cbrt(s);

            return new[]
            {
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
            };
        }

        private static double luminance(double[] srgb)
        {
            double[] linear = toLinear(srgb);
            return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
        }
    }
}
