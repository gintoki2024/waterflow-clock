using System;
using System.Drawing;
using System.Drawing.Drawing2D;

internal static class EffectProcedural
{
    private const int Size = 128;
    private const int Mask = Size - 1;
    private static readonly float[] FbmLut = new float[Size * Size];
    private static readonly float[] RidgeLut = new float[Size * Size];

    static EffectProcedural()
    {
        // Precompute seamless tiling 4-octave FBM & Ridged noise LUTs (128x128)
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float fbm = 0f;
                float ridge = 0f;
                float amp = 0.5f;
                int freq = 4;
                float norm = 0f;

                for (int oct = 0; oct < 4; oct++)
                {
                    float v = TileValueNoise(x * freq / (float)Size, y * freq / (float)Size, freq, oct * 101 + 17);
                    fbm += v * amp;
                    float r = 1f - Math.Abs(v * 2f - 1f);
                    ridge += (r * r) * amp;
                    norm += amp;
                    amp *= 0.5f;
                    freq <<= 1;
                }

                FbmLut[y * Size + x] = fbm / norm;
                RidgeLut[y * Size + x] = ridge / norm;
            }
        }
    }

    public static float SampleFbm(float x, float y)
    {
        return SampleLut(FbmLut, x, y);
    }

    public static float SampleRidge(float x, float y)
    {
        return SampleLut(RidgeLut, x, y);
    }

    // Interference of two drifting ridged-noise sheets produces realistic underwater caustics
    public static float SampleCaustic(float x, float y, float timeSec)
    {
        float a = SampleRidge(x * 0.42f + timeSec * 4.2f, y * 0.42f - timeSec * 3.1f);
        float b = SampleRidge(x * 0.58f - timeSec * 3.4f + 37f, y * 0.58f + timeSec * 2.6f + 19f);
        float c = a * b;
        // Sharpen bright caustic filaments
        float peak = Math.Max(0f, c - 0.36f) * 2.4f;
        return Math.Min(1f, peak * peak);
    }

    public static float Hash01(int n)
    {
        unchecked
        {
            uint x = (uint)n;
            x = (x ^ 61) ^ (x >> 16);
            x += x << 3;
            x ^= x >> 4;
            x *= 0x27d4eb2d;
            x ^= x >> 15;
            return (x & 0xFFFF) / 65535f;
        }
    }

    private static float SampleLut(float[] lut, float x, float y)
    {
        float sx = x + 65536f;
        float sy = y + 65536f;
        int ix = (int)sx;
        int iy = (int)sy;
        float tx = sx - ix;
        float ty = sy - iy;
        int x0 = ix & Mask;
        int y0 = iy & Mask;
        int x1 = (x0 + 1) & Mask;
        int y1 = (y0 + 1) & Mask;

        int r0 = y0 << 7;
        int r1 = y1 << 7;
        float top = lut[r0 + x0] + (lut[r0 + x1] - lut[r0 + x0]) * tx;
        float bot = lut[r1 + x0] + (lut[r1 + x1] - lut[r1 + x0]) * tx;
        return top + (bot - top) * ty;
    }

    private static float TileValueNoise(float x, float y, int period, int seed)
    {
        int x0 = ((int)Math.Floor(x)) % period;
        if (x0 < 0) x0 += period;
        int y0 = ((int)Math.Floor(y)) % period;
        if (y0 < 0) y0 += period;
        int x1 = (x0 + 1) % period;
        int y1 = (y0 + 1) % period;

        float tx = x - (float)Math.Floor(x);
        float ty = y - (float)Math.Floor(y);
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        float c00 = Hash01(x0 * 374761393 + y0 * 668265263 + seed * 1442695041);
        float c10 = Hash01(x1 * 374761393 + y0 * 668265263 + seed * 1442695041);
        float c01 = Hash01(x0 * 374761393 + y1 * 668265263 + seed * 1442695041);
        float c11 = Hash01(x1 * 374761393 + y1 * 668265263 + seed * 1442695041);

        float top = c00 + (c10 - c00) * tx;
        float bot = c01 + (c11 - c01) * tx;
        return top + (bot - top) * ty;
    }

    // =========================================================================
    // Dedicated Scene Background Material Styling (applied into cachedBgPixels)
    // =========================================================================
    public static void ApplySceneSurfacePixel(ClockFlowStyle style, ClockTheme theme,
        int x, int y, RectangleF cardRect, ref double r, ref double g, ref double b)
    {
        float u = (x - cardRect.Left) / Math.Max(1f, cardRect.Width);
        float v = (y - cardRect.Top) / Math.Max(1f, cardRect.Height);
        bool light = (theme == ClockTheme.PorcelainLight);

        switch (style)
        {
            case ClockFlowStyle.LiquidStream:
            {
                // Subtle aquatic glass depth gradient (cool crystal top, deeper abyssal bottom)
                double depth = Math.Abs(v - 0.5) * 2.0;
                if (!light)
                {
                    r = r * (0.94 - 0.08 * depth);
                    g = g * (0.98 - 0.04 * depth) + 5.0 * (1.0 - depth);
                    b = b * 1.04 + 12.0 * (1.0 - depth * 0.5);
                }
                else
                {
                    r = r - 4.0 * (1.0 - depth);
                    g = g + 2.0 * (1.0 - depth);
                    b = Math.Min(255.0, b + 6.0 * (1.0 - depth));
                }
                break;
            }

            case ClockFlowStyle.Stardust:
            {
                // Cosmic midnight aurora nebula with gentle violet/cyan stardust haze
                float neb1 = SampleFbm(x * 0.22f + 11f, y * 0.22f + 7f);
                float neb2 = SampleRidge(x * 0.34f + 31f, y * 0.34f + 19f);
                float aurora = Math.Max(0f, (neb1 * 0.65f + neb2 * 0.35f) - 0.32f) * 1.45f;
                if (!light)
                {
                    double mix = 0.56;
                    double baseR = 12.0 + aurora * (38.0 + 26.0 * u);
                    double baseG = 14.0 + aurora * (28.0 + 34.0 * (1f - v));
                    double baseB = 28.0 + aurora * (68.0 + 28.0 * (1f - u));
                    r = r * (1.0 - mix) + baseR * mix;
                    g = g * (1.0 - mix) + baseG * mix;
                    b = b * (1.0 - mix) + baseB * mix;
                }
                else
                {
                    // Soft champagne-violet stardust sheen for PorcelainLight
                    r += aurora * 10.0;
                    g += aurora * 6.0;
                    b += aurora * 14.0;
                }
                break;
            }

            case ClockFlowStyle.IceShards:
            {
                // Organic frosted glacial glass: soft noise-modulated frost clinging to the outer rim & corners
                float dx = Math.Abs(u - 0.5f) * 2f;
                float dy = Math.Abs(v - 0.5f) * 2f;
                float radial = (float)Math.Sqrt(dx * dx * 0.78f + dy * dy * 1.15f);
                float crystal = SampleRidge(x * 0.52f, y * 0.52f) * 0.65f + SampleFbm(x * 0.95f, y * 0.95f) * 0.35f;
                float organicEdge = Math.Max(0f, (radial - 0.58f + (crystal - 0.5f) * 0.28f) / 0.55f);
                float frost = Math.Max(0f, Math.Min(1f, organicEdge * organicEdge * (0.45f + 0.75f * crystal)));

                if (!light)
                {
                    // Cold deep sapphire-ice glass + delicate organic crystal frost along outer edges
                    r = r * 0.86 + frost * 72.0;
                    g = g * 0.96 + 7.0 + frost * 102.0;
                    b = b * 1.06 + 16.0 + frost * 124.0;
                }
                else
                {
                    r = r - 4.0 + frost * 12.0;
                    g = g + frost * 16.0;
                    b = Math.Min(255.0, b + 6.0 + frost * 22.0);
                }
                break;
            }

            case ClockFlowStyle.SplitFlap:
            {
                // Brushed anodized aluminum Solari housing (fine horizontal machined grain)
                float brush = SampleFbm(x * 0.045f, y * 1.45f) - 0.5f;
                float sheen = (1f - v) * 20f;
                if (!light)
                {
                    double baseLum = 28.0 + sheen + brush * 14.0;
                    r = r * 0.28 + baseLum * 0.72;
                    g = g * 0.28 + (baseLum + 2.0) * 0.72;
                    b = b * 0.28 + (baseLum + 6.0) * 0.72;
                }
                else
                {
                    double baseLum = 228.0 + sheen * 0.5 + brush * 10.0;
                    r = r * 0.25 + baseLum * 0.75;
                    g = g * 0.25 + (baseLum + 1.0) * 0.75;
                    b = b * 0.25 + (baseLum + 3.0) * 0.75;
                }
                break;
            }
        }

        r = Math.Max(0.0, Math.Min(255.0, r));
        g = Math.Max(0.0, Math.Min(255.0, g));
        b = Math.Max(0.0, Math.Min(255.0, b));
    }

    // Architectural vector details drawn over the cached scene card
    public static void DrawSceneCardDetails(Graphics g, ClockFlowStyle style, ClockTheme theme,
        RectangleF cardRect, float cornerRadius, float centerY, float rowHeight)
    {
        bool light = (theme == ClockTheme.PorcelainLight);

        if (style == ClockFlowStyle.SplitFlap)
        {
            // Recessed mechanical bay window frame + 4 machined corner screws
            float inset = Math.Max(7f, cardRect.Height * 0.05f);
            RectangleF bay = new RectangleF(
                cardRect.Left + inset, cardRect.Top + inset,
                cardRect.Width - inset * 2f, cardRect.Height - inset * 2f);

            using (var bayPath = CreateRoundRect(bay, Math.Max(4f, cornerRadius - inset * 0.7f)))
            {
                using (var recess = new SolidBrush(Color.FromArgb(light ? 22 : 76, 0, 2, 6)))
                    g.FillPath(recess, bayPath);
                using (var innerShadow = new Pen(Color.FromArgb(light ? 45 : 120, 0, 0, 0), 1.6f))
                    g.DrawPath(innerShadow, bayPath);
            }

            // Four tiny machined corner fasteners
            float screwR = Math.Max(2.2f, Math.Min(3.8f, inset * 0.34f));
            float sx1 = cardRect.Left + inset * 1.1f;
            float sx2 = cardRect.Right - inset * 1.1f;
            float sy1 = cardRect.Top + inset * 1.1f;
            float sy2 = cardRect.Bottom - inset * 1.1f;
            PointF[] screws = { new PointF(sx1, sy1), new PointF(sx2, sy1), new PointF(sx1, sy2), new PointF(sx2, sy2) };
            foreach (PointF sc in screws)
            {
                using (var head = new SolidBrush(light ? Color.FromArgb(165, 148, 156, 168) : Color.FromArgb(190, 56, 64, 78)))
                    g.FillEllipse(head, sc.X - screwR, sc.Y - screwR, screwR * 2f, screwR * 2f);
                using (var slot = new Pen(light ? Color.FromArgb(140, 82, 90, 102) : Color.FromArgb(180, 14, 18, 26), 0.9f))
                    g.DrawLine(slot, sc.X - screwR * 0.65f, sc.Y - screwR * 0.35f, sc.X + screwR * 0.65f, sc.Y + screwR * 0.35f);
            }
        }
        else if (style == ClockFlowStyle.Stardust)
        {
            // Delicate scattered pinpoint stars across the cosmic nebula card
            for (int i = 0; i < 24; i++)
            {
                float hx = Hash01(i * 47 + 13);
                float hy = Hash01(i * 83 + 29);
                float hb = Hash01(i * 61 + 5);
                float sx = cardRect.Left + 10f + hx * Math.Max(4f, cardRect.Width - 20f);
                float sy = cardRect.Top + 10f + hy * Math.Max(4f, cardRect.Height - 20f);
                float r = 0.75f + hb * 1.15f;
                int a = (int)((light ? 55 : 115) * (0.45f + 0.55f * hb));
                Color starCol = i % 3 == 0 ? Color.FromArgb(a, 255, 232, 175)
                    : i % 3 == 1 ? Color.FromArgb(a, 155, 232, 255)
                    : Color.FromArgb(a, 212, 194, 255);
                using (var br = new SolidBrush(starCol))
                    g.FillEllipse(br, sx - r, sy - r, r * 2f, r * 2f);
            }
        }
    }

    private static GraphicsPath CreateRoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
        GraphicsPath path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
