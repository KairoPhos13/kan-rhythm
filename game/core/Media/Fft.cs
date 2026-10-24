// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Media
{
    public static class Fft
    {
        /// <summary>In-place iterative radix-2 FFT (length must be a power of two).</summary>
        public static void Transform(float[] re, float[] im)
        {
            int n = re.Length;

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);

                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;

                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        float tr = re[b] * cr - im[b] * ci;
                        float ti = re[b] * ci + im[b] * cr;

                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;

                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
