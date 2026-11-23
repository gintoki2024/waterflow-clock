using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

internal sealed partial class ClockRenderer
{
    private void PaintTransition(Graphics g, Digit digit, float x, Color ink, double elapsed)
    {
        double duration = flowStyle == ClockFlowStyle.SplitFlap ? 640.0
            : flowStyle == ClockFlowStyle.IceShards ? 880.0
            : 860.0;

        float t = digit.OldValue == 0 ? 1f : (float)Math.Max(0.0, Math.Min(1.0, elapsed / duration));
        GraphicsState state = g.Save();
        try
        {
            g.TranslateTransform(x, centerY);
            float w = digitWidth * 1.12f;
            float h = digitHeight * 1.12f;

            // Keep effects strictly inside the changing column horizontally so adjacent fixed digits are never touched,
            // while allowing generous vertical travel into the upper/lower reserved zones.
            g.SetClip(new RectangleF(-w * 0.54f, -h * 1.32f, w * 1.08f, h * 2.64f), CombineMode.Intersect);

            GraphicsPath next = glyphs[digit.Value - '0'];
            GraphicsPath previous = digit.OldValue == 0 ? next : glyphs[digit.OldValue - '0'];
            int seed = (digit.Value - '0') * 19 + (digit.OldValue == 0 ? 0 : (digit.OldValue - '0') * 7);

            if (flowStyle == ClockFlowStyle.SplitFlap)
            {
                PaintFlap3D(g, previous, next, ink, w, h, t);
            }
            else if (t <= 0f || t >= 1f)
            {
                PaintRestingGlyph(g, t <= 0f ? previous : next, ink);
            }
            else if (flowStyle == ClockFlowStyle.EmberBurn)
            {
                PaintEmberFbm(g, previous, next, ink, w, h, t, seed);
            }
            else
            {
                PaintIceVoronoi3D(g, previous, next, ink, w, h, t, seed);
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void PaintRestingGlyph(Graphics g, GraphicsPath path, Color ink)
    {
        if (theme == ClockTheme.PureFloating)
        {
            GraphicsState s = g.Save();
            try
            {
                g.TranslateTransform(0f, 3.2f);
                using (var shadow = new SolidBrush(Color.FromArgb(100, 8, 12, 20)))
                    g.FillPath(shadow, path);
            }
            finally { g.Restore(s); }
        }
        FillGlyph(g, path, ink);
    }

    private static void FillGlyph(Graphics g, GraphicsPath path, Color ink)
    {
        if (ink.A <= 1) return;
        using (var brush = new SolidBrush(ink))
            g.FillPath(brush, path);
    }

    private static Color MixInk(Color a, Color b, float t, float opacity)
    {
        t = Math.Max(0f, Math.Min(1f, t));
        opacity = Math.Max(0f, Math.Min(1f, opacity));
        int alpha = Math.Max(0, Math.Min(255, (int)Math.Round(a.A * opacity)));
        return Color.FromArgb(
            alpha,
            Math.Max(0, Math.Min(255, (int)Math.Round(a.R + (b.R - a.R) * t))),
            Math.Max(0, Math.Min(255, (int)Math.Round(a.G + (b.G - a.G) * t))),
            Math.Max(0, Math.Min(255, (int)Math.Round(a.B + (b.B - a.B) * t))));
    }

    // =========================================================================
    // 1. EMBER BURN: High-compute per-pixel 2D FBM noise erosion, 5-stage blackbody
    //    radiation (ink -> char -> crimson -> blaze orange -> white-gold core),
    //    dynamic obsidian firelight bloom, heat haze, and 38 turbulent ember + ash particles.
    // =========================================================================
    private void PaintEmberFbm(Graphics g, GraphicsPath oldFace, GraphicsPath next, Color ink,
        float w, float h, float t, int seed)
    {
        bool lightTheme = ink.GetBrightness() < 0.5f;
        float energy = (float)Math.Pow(Math.Sin(t * Math.PI), 0.70);
        float burnProgress = -0.18f + 1.38f * (t * t * (3f - 2f * t));

        // 1. Dynamic warm firelight glow cast onto the background behind the burning column
        if (theme != ClockTheme.PureFloating && !lightTheme && energy > 0.04f)
        {
            float frontY = h * (0.58f - 1.16f * (t * t * (3f - 2f * t)));
            float glowR = Math.Max(w * 0.52f, h * 0.58f);
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(-glowR, frontY - glowR * 0.85f, glowR * 2f, glowR * 1.7f);
                using (var pgb = new PathGradientBrush(gp))
                {
                    pgb.CenterColor = Color.FromArgb((int)(92 * energy), 255, 92, 18);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, 255, 60, 10) };
                    g.FillPath(pgb, gp);
                }
            }
        }

        // 2. Per-pixel 2D FBM noise erosion of oldFace & molten forging reveal of next (reusing scratch buffers!)
        int bw = Math.Max(16, (int)Math.Ceiling(w * 1.06f));
        int bh = Math.Max(20, (int)Math.Ceiling(h * 1.24f));
        float cx = bw * 0.5f;
        float cy = bh * 0.5f;

        if (emberOldBmp == null || emberBufW != bw || emberBufH != bh)
        {
            if (emberOldBmp != null) emberOldBmp.Dispose();
            if (emberNewBmp != null) emberNewBmp.Dispose();
            if (emberOutBmp != null) emberOutBmp.Dispose();
            emberOldBmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            emberNewBmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            emberOutBmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            emberOldBuf = new byte[bw * bh * 4];
            emberNewBuf = new byte[bw * bh * 4];
            emberOutBuf = new byte[bw * bh * 4];
            emberBufW = bw;
            emberBufH = bh;
        }

        using (var og = Graphics.FromImage(emberOldBmp))
        {
            og.Clear(Color.Transparent);
            og.SmoothingMode = SmoothingMode.HighQuality;
            og.TranslateTransform(cx, cy);
            FillGlyph(og, oldFace, Color.White);
        }
        using (var ng = Graphics.FromImage(emberNewBmp))
        {
            ng.Clear(Color.Transparent);
            ng.SmoothingMode = SmoothingMode.HighQuality;
            ng.TranslateTransform(cx, cy);
            FillGlyph(ng, next, Color.White);
        }

        Rectangle rect = new Rectangle(0, 0, bw, bh);
        byte[] oldBuf = emberOldBuf;
        byte[] newBuf = emberNewBuf;
        byte[] outBuf = emberOutBuf;
        Array.Clear(outBuf, 0, outBuf.Length);

        BitmapData oData = emberOldBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData nData = emberNewBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(oData.Scan0, oldBuf, 0, oldBuf.Length);
            Marshal.Copy(nData.Scan0, newBuf, 0, newBuf.Length);
        }
        finally
        {
            emberOldBmp.UnlockBits(oData);
            emberNewBmp.UnlockBits(nData);
        }

        float noiseScale = 44f / Math.Max(28f, h);
        float seedOffX = (seed * 13.7f) % 97f;
        float seedOffY = (seed * 23.3f) % 89f;
        float timeFlow = t * 11.5f;
        float heatResidual = (float)Math.Pow(1f - t, 1.45) * 0.52f;

        for (int py = 0; py < bh; py++)
        {
            float v = 1f - (py / (float)bh);
            bool nearFront = Math.Abs(v - burnProgress) < 0.42f;
            int rowOff = py * bw * 4;

            for (int px = 0; px < bw; px++)
            {
                int idx = rowOff + (px << 2);
                int oa = oldBuf[idx + 3];
                int na = newBuf[idx + 3];
                if (oa == 0 && na == 0) continue;

                float d;
                if (nearFront)
                {
                    float n = EffectProcedural.SampleFbm(px * noiseScale + seedOffX, py * noiseScale - timeFlow + seedOffY);
                    float fine = EffectProcedural.SampleRidge(px * noiseScale * 1.9f - seedOffY, py * noiseScale * 1.9f - timeFlow * 1.6f);
                    d = (v + (n - 0.5f) * 0.30f + (fine - 0.5f) * 0.14f) - burnProgress;
                }
                else
                {
                    d = v - burnProgress;
                }

                int fr = 0, fg = 0, fb = 0, fa = 0;

                if (oa > 0 && d > -0.025f)
                {
                    if (d > 0.155f)
                    {
                        fr = ink.R; fg = ink.G; fb = ink.B; fa = (oa * ink.A) / 255;
                    }
                    else if (d > 0.085f)
                    {
                        float k = (0.155f - d) / 0.070f;
                        fr = (int)(ink.R + (24 - ink.R) * k * 0.88f);
                        fg = (int)(ink.G + (10 - ink.G) * k * 0.90f);
                        fb = (int)(ink.B + (8 - ink.B) * k * 0.92f);
                        fa = (oa * ink.A) / 255;
                    }
                    else if (d > 0.038f)
                    {
                        float k = (0.085f - d) / 0.047f;
                        fr = (int)(24 + (225 - 24) * k);
                        fg = (int)(10 + (46 - 10) * k);
                        fb = (int)(8 + (10 - 8) * k);
                        fa = oa;
                    }
                    else if (d > 0.002f)
                    {
                        float k = (0.038f - d) / 0.036f;
                        fr = 255;
                        fg = (int)(92 + (242 - 92) * k);
                        fb = (int)(18 + (192 - 18) * k);
                        fa = oa;
                    }
                    else
                    {
                        float k = (d + 0.025f) / 0.027f;
                        fr = 255;
                        fg = (int)(120 + 110 * k);
                        fb = (int)(28 + 130 * k);
                        fa = (int)(oa * Math.Max(0f, k));
                    }
                }

                if (na > 0 && d < 0.015f)
                {
                    float reveal = Math.Max(0f, Math.Min(1f, (-d + 0.015f) / 0.095f));
                    float rimHot = Math.Max(0f, 1f - (-d) / 0.085f) * energy;
                    int nr = (int)(ink.R + (255 - ink.R) * Math.Min(1f, heatResidual + rimHot * 0.85f));
                    int ng = (int)(ink.G + (162 - ink.G) * Math.Min(1f, heatResidual + rimHot * 0.65f));
                    int nb = (int)(ink.B + (48 - ink.B) * Math.Min(1f, heatResidual + rimHot * 0.80f));
                    int nAlpha = (int)((na * ink.A / 255f) * Math.Pow(reveal, 0.72));

                    if (fa == 0)
                    {
                        fr = nr; fg = ng; fb = nb; fa = nAlpha;
                    }
                    else if (nAlpha > 0)
                    {
                        float sa = fa / 255f;
                        float inv = 1f - sa;
                        fr = (int)Math.Min(255f, fr * sa + nr * inv);
                        fg = (int)Math.Min(255f, fg * sa + ng * inv);
                        fb = (int)Math.Min(255f, fb * sa + nb * inv);
                        fa = Math.Min(255, fa + (int)(nAlpha * inv));
                    }
                }

                outBuf[idx] = (byte)Math.Max(0, Math.Min(255, fb));
                outBuf[idx + 1] = (byte)Math.Max(0, Math.Min(255, fg));
                outBuf[idx + 2] = (byte)Math.Max(0, Math.Min(255, fr));
                outBuf[idx + 3] = (byte)Math.Max(0, Math.Min(255, fa));
            }
        }

        BitmapData outData = emberOutBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(outBuf, 0, outData.Scan0, outBuf.Length); }
        finally { emberOutBmp.UnlockBits(outData); }

        g.DrawImageUnscaled(emberOutBmp, (int)Math.Round(-cx), (int)Math.Round(-cy));

        // 3. 38 turbulent rising glowing embers, velocity-stretched spark streaks, and charred ash flakes
        float baseFrontY = h * (0.60f - 1.20f * (t * t * (3f - 2f * t)));
        const int particleCount = 38;
        for (int j = 0; j < particleCount; j++)
        {
            float h1 = EffectProcedural.Hash01(seed * 17 + j * 43);
            float h2 = EffectProcedural.Hash01(seed * 31 + j * 61);
            float h3 = EffectProcedural.Hash01(seed * 13 + j * 89);

            float phase = (t * (1.25f + h2 * 1.15f) + h1 * 0.92f) % 1f;
            float lifeAlpha = energy * (float)Math.Sin(phase * Math.PI);
            if (lifeAlpha <= 0.04f) continue;

            float startX = (h1 - 0.5f) * w * 0.86f;
            float turb = (EffectProcedural.SampleFbm(j * 3.7f, phase * 8.0f + t * 4f) - 0.5f) * w * 0.24f;
            float px = startX + turb + (float)Math.Sin(phase * 7.2f + j * 1.9f) * w * 0.045f;
            float rise = h * (0.04f + (0.42f + 0.44f * h3) * phase * (0.75f + 0.45f * phase));
            float py = baseFrontY + (h2 - 0.5f) * h * 0.10f - rise;

            if (j % 6 == 0)
            {
                // Tumbling dark charred ash flake
                float aw = Math.Max(1.1f, h * (0.015f + 0.012f * h2));
                float ah = aw * (0.45f + 0.45f * (float)Math.Abs(Math.Cos(phase * 9f + j)));
                GraphicsState ashState = g.Save();
                g.TranslateTransform(px, py);
                g.RotateTransform(phase * 220f + j * 45f);
                using (var ash = new SolidBrush(Color.FromArgb((int)(155 * lifeAlpha), 34, 24, 22)))
                    g.FillEllipse(ash, -aw, -ah, aw * 2f, ah * 2f);
                g.Restore(ashState);
            }
            else
            {
                // Blackbody cooling spark: white-gold -> bright orange -> deep ember red
                Color coreColor = phase < 0.30f
                    ? MixInk(Color.FromArgb(255, 255, 250, 212), Color.FromArgb(255, 255, 162, 42), phase / 0.30f, lifeAlpha)
                    : MixInk(Color.FromArgb(255, 255, 162, 42), Color.FromArgb(255, 215, 42, 12), (phase - 0.30f) / 0.70f, lifeAlpha);

                float r = Math.Max(0.75f, h * (0.011f + 0.012f * h2) * (1f - 0.55f * phase));

                // Soft luminous bloom halo
                using (var halo = new SolidBrush(Color.FromArgb((int)(72 * lifeAlpha), 255, 108, 22)))
                    g.FillEllipse(halo, px - r * 2.5f, py - r * 2.5f, r * 5.0f, r * 5.0f);

                // Velocity-stretched glowing spark streak
                float streakLen = r * (2.2f + 2.4f * h3);
                using (var streak = new Pen(coreColor, r * 1.45f))
                {
                    streak.StartCap = streak.EndCap = LineCap.Round;
                    g.DrawLine(streak, px, py, px - turb * 0.09f, py + streakLen);
                }
            }
        }
    }

    // =========================================================================
    // 2. ICE SHARDS: Branching frost-crack propagation -> 20-cell Voronoi crystal
    //    shatter with 3D pitch/yaw/roll tumbling, Fresnel specular facet flashes,
    //    cold frost mist, and 28 sparkling diamond ice dust particles.
    // =========================================================================
    private static readonly PointF[][] iceVoronoi = BuildVoronoiCells20();

    private static PointF[][] BuildVoronoiCells20()
    {
        // Generate 20 deterministic Voronoi crystal polygons clipped to [-0.62, 0.62]^2
        // clustered slightly denser around the off-center fracture epicenter (-0.08, -0.04)
        const int count = 20;
        PointF[] sites = new PointF[count];
        for (int i = 0; i < count; i++)
        {
            float ring = i < 5 ? 0.19f : (i < 12 ? 0.40f : 0.58f);
            double ang = (i < 5 ? i * Math.PI * 2.0 / 5.0
                : i < 12 ? (i - 5) * Math.PI * 2.0 / 7.0 + 0.35
                : (i - 12) * Math.PI * 2.0 / 8.0 + 0.18);
            float jitterR = (EffectProcedural.Hash01(i * 53 + 11) - 0.5f) * 0.11f;
            float jitterA = (EffectProcedural.Hash01(i * 97 + 29) - 0.5f) * 0.28f;
            float r = Math.Max(0.05f, ring + jitterR);
            sites[i] = new PointF(
                -0.06f + (float)Math.Cos(ang + jitterA) * r,
                -0.03f + (float)Math.Sin(ang + jitterA) * r);
        }

        PointF[][] cells = new PointF[count][];
        for (int i = 0; i < count; i++)
        {
            var poly = new List<PointF>
            {
                new PointF(-0.62f, -0.62f),
                new PointF(0.62f, -0.62f),
                new PointF(0.62f, 0.62f),
                new PointF(-0.62f, 0.62f)
            };

            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                poly = ClipHalfPlane(poly, sites[i], sites[j]);
                if (poly.Count < 3) break;
            }
            cells[i] = poly.ToArray();
        }
        return cells;
    }

    private static List<PointF> ClipHalfPlane(List<PointF> poly, PointF keepSite, PointF otherSite)
    {
        // Keep points closer to keepSite than otherSite: (p - mid) . (keep - other) >= 0
        float mx = (keepSite.X + otherSite.X) * 0.5f;
        float my = (keepSite.Y + otherSite.Y) * 0.5f;
        float nx = keepSite.X - otherSite.X;
        float ny = keepSite.Y - otherSite.Y;

        var output = new List<PointF>(poly.Count + 1);
        for (int i = 0; i < poly.Count; i++)
        {
            PointF a = poly[i];
            PointF b = poly[(i + 1) % poly.Count];
            float da = (a.X - mx) * nx + (a.Y - my) * ny;
            float db = (b.X - mx) * nx + (b.Y - my) * ny;

            if (da >= 0f)
            {
                output.Add(a);
                if (db < 0f)
                {
                    float s = da / (da - db);
                    output.Add(new PointF(a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s));
                }
            }
            else if (db >= 0f)
            {
                float s = da / (da - db);
                output.Add(new PointF(a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s));
            }
        }
        return output;
    }

    private void PaintIceVoronoi3D(Graphics g, GraphicsPath oldFace, GraphicsPath next, Color ink,
        float w, float h, float t, int seed)
    {
        bool lightTheme = ink.GetBrightness() < 0.5f;
        Color iceDeep = lightTheme ? Color.FromArgb(255, 22, 112, 168) : Color.FromArgb(255, 118, 214, 255);
        Color iceBright = lightTheme ? Color.FromArgb(255, 185, 236, 255) : Color.FromArgb(255, 238, 252, 255);

        float crackPhase = Math.Min(1f, t / 0.25f);
        float shatter = Math.Max(0f, Math.Min(1f, (t - 0.19f) / 0.59f));
        float assemble = Math.Max(0f, Math.Min(1f, (t - 0.35f) / 0.65f));

        // 1. Cold glacial mist plume expanding at the moment of shatter
        float mistEnv = (float)Math.Sin(Math.Max(0f, Math.Min(1f, (t - 0.14f) / 0.66f)) * Math.PI);
        if (theme != ClockTheme.PureFloating && mistEnv > 0.05f)
        {
            float mr = Math.Max(w, h) * (0.36f + 0.34f * shatter);
            using (var mp = new GraphicsPath())
            {
                mp.AddEllipse(-mr, -mr * 0.9f, mr * 2f, mr * 1.8f);
                using (var pgb = new PathGradientBrush(mp))
                {
                    pgb.CenterColor = Color.FromArgb((int)((lightTheme ? 42 : 68) * mistEnv), iceDeep);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, iceDeep) };
                    g.FillPath(pgb, mp);
                }
            }
        }

        // 2. Departing digit: 20 Voronoi crystal shards tumbling in 3D with Fresnel specular facet flashes
        if (shatter < 1f)
        {
            float burstEase = shatter * shatter * (1.75f - 0.75f * shatter);
            float opacity = shatter <= 0f ? 1f : (float)Math.Pow(1f - shatter, 0.80);

            for (int i = 0; i < iceVoronoi.Length; i++)
            {
                PointF[] poly = iceVoronoi[i];
                if (poly.Length < 3) continue;
                PointF c = PolyCentroid(poly);

                float h1 = EffectProcedural.Hash01(seed + i * 29);
                float h2 = EffectProcedural.Hash01(seed + i * 53 + 11);
                float h3 = EffectProcedural.Hash01(seed + i * 71 + 3);

                float tx = c.X * w * (0.58f + 0.45f * h1) * burstEase;
                float ty = c.Y * h * (0.52f + 0.40f * h2) * burstEase + h * (0.38f + 0.24f * h3) * shatter * shatter;

                // 3D tumbling angles (roll, pitch, yaw)
                float roll = ((i % 2 == 0) ? 1f : -1f) * (22f + 42f * h1) * burstEase;
                double pitch = ((i % 3 == 0) ? 1.0 : -1.0) * (0.75 + 1.15 * h2) * shatter;
                double yaw = ((i % 2 == 0) ? -1.0 : 1.0) * (0.65 + 1.05 * h3) * shatter;

                float scaleX = (float)Math.Max(0.24, Math.Abs(Math.Cos(yaw))) * (1f - 0.14f * shatter);
                float scaleY = (float)Math.Max(0.24, Math.Abs(Math.Cos(pitch))) * (1f - 0.14f * shatter);

                // Fresnel specular flash when a tumbling crystal facet catches overhead light
                float facetGlint = (float)Math.Pow(Math.Abs(Math.Sin(pitch * 1.4 + yaw * 1.1 + i * 0.7)), 5.0) * shatter;

                GraphicsState saved = g.Save();
                try
                {
                    g.TranslateTransform(c.X * w + tx, c.Y * h + ty);
                    g.RotateTransform(roll);
                    g.ScaleTransform(scaleX, scaleY);
                    g.TranslateTransform(-c.X * w, -c.Y * h);

                    using (var cellPath = ScalePoly(poly, w, h))
                    {
                        g.SetClip(cellPath, CombineMode.Intersect);

                        float frostMix = Math.Min(0.92f, crackPhase * 0.48f + shatter * 0.55f + (i % 3) * 0.07f);
                        Color baseShard = MixInk(ink, iceDeep, frostMix, opacity);
                        Color litShard = MixInk(baseShard, iceBright, facetGlint * 0.85f, 1f);
                        FillGlyph(g, oldFace, litShard);

                        // Razor-sharp crystalline cut-edge bevel highlight
                        float rimVis = Math.Min(1f, crackPhase * 0.85f + facetGlint * 0.7f + (1f - shatter) * 0.35f) * opacity;
                        if (rimVis > 0.06f)
                        {
                            g.SetClip(oldFace, CombineMode.Intersect);
                            using (var bevel = new Pen(Color.FromArgb((int)(230 * rimVis), iceBright), Math.Max(1.0f, h * 0.022f)))
                            {
                                bevel.LineJoin = LineJoin.Miter;
                                g.DrawPath(bevel, cellPath);
                            }
                        }
                    }
                }
                finally { g.Restore(saved); }
            }

            // Branching lightning-like frost fracture network flashing across glyph before shatter
            float crackFlash = (float)Math.Sin(Math.Max(0f, Math.Min(1f, (t - 0.03f) / 0.27f)) * Math.PI);
            if (crackFlash > 0.05f)
            {
                GraphicsState fState = g.Save();
                try
                {
                    g.SetClip(oldFace, CombineMode.Intersect);
                    PointF epi = new PointF(-0.08f * w, -0.04f * h);
                    using (var glowPen = new Pen(Color.FromArgb((int)(180 * crackFlash), iceDeep), Math.Max(2.5f, h * 0.050f)))
                    using (var corePen = new Pen(Color.FromArgb((int)(250 * crackFlash), iceBright), Math.Max(1.0f, h * 0.017f)))
                    {
                        glowPen.LineJoin = corePen.LineJoin = LineJoin.Miter;
                        for (int r = 0; r < 7; r++)
                        {
                            double ang = r * (Math.PI * 2.0 / 7.0) + (seed % 7) * 0.22;
                            float reach = (0.38f + 0.35f * crackPhase) * Math.Max(w, h);
                            PointF p1 = new PointF(epi.X + (float)Math.Cos(ang + 0.16) * reach * 0.42f, epi.Y + (float)Math.Sin(ang + 0.16) * reach * 0.42f);
                            PointF p2 = new PointF(epi.X + (float)Math.Cos(ang - 0.08) * reach * 0.78f, epi.Y + (float)Math.Sin(ang - 0.08) * reach * 0.78f);
                            PointF p3 = new PointF(epi.X + (float)Math.Cos(ang + 0.06) * reach, epi.Y + (float)Math.Sin(ang + 0.06) * reach);
                            g.DrawLines(glowPen, new[] { epi, p1, p2, p3 });
                            g.DrawLines(corePen, new[] { epi, p1, p2, p3 });

                            // Secondary forked crack branch
                            PointF fork = new PointF(p1.X + (float)Math.Cos(ang + 0.62) * reach * 0.38f, p1.Y + (float)Math.Sin(ang + 0.62) * reach * 0.38f);
                            g.DrawLine(glowPen, p1, fork);
                            g.DrawLine(corePen, p1, fork);
                        }
                    }
                }
                finally { g.Restore(fState); }
            }
        }

        // 3. 28 sparkling diamond ice dust crystals & refracted glints
        float dustEnv = (float)Math.Sin(Math.Max(0f, Math.Min(1f, (t - 0.15f) / 0.74f)) * Math.PI);
        if (dustEnv > 0.05f)
        {
            for (int k = 0; k < 28; k++)
            {
                float h1 = EffectProcedural.Hash01(seed * 19 + k * 37);
                float h2 = EffectProcedural.Hash01(seed * 11 + k * 53);
                float h3 = EffectProcedural.Hash01(seed * 23 + k * 71);
                double ang = h1 * Math.PI * 2.0;
                float dist = (0.12f + 0.50f * shatter) * (0.55f + 0.75f * h2);
                float px = (float)Math.Cos(ang) * w * dist;
                float py = (float)Math.Sin(ang) * h * dist + h * 0.24f * shatter * shatter;
                float twinkle = 0.55f + 0.45f * (float)Math.Sin(t * 24f + k * 1.7f);
                float sr = Math.Max(0.85f, h * (0.010f + 0.013f * h3) * dustEnv * twinkle);

                PointF[] diamond =
                {
                    new PointF(px, py - sr * 1.55f),
                    new PointF(px + sr, py),
                    new PointF(px, py + sr * 1.55f),
                    new PointF(px - sr, py)
                };
                using (var br = new SolidBrush(Color.FromArgb((int)(235 * dustEnv * twinkle), k % 2 == 0 ? iceBright : iceDeep)))
                    g.FillPolygon(br, diamond);
            }
        }

        // 4. Incoming digit: 20 shards converge cleanly from frost mist and thaw into crisp ink
        if (assemble > 0f)
        {
            float inv = 1f - assemble;
            float spread = inv * inv * inv;
            float arriveOpacity = (float)Math.Pow(assemble, 0.52);

            for (int i = 0; i < iceVoronoi.Length; i++)
            {
                PointF[] poly = iceVoronoi[i];
                if (poly.Length < 3) continue;
                PointF c = PolyCentroid(poly);
                float h1 = EffectProcedural.Hash01(seed + i * 31 + 5);

                float ox = c.X * w * (0.46f + 0.28f * h1) * spread;
                float oy = c.Y * h * (0.42f + 0.26f * h1) * spread - h * 0.14f * spread;
                float roll = ((i % 2 == 0) ? -1f : 1f) * (18f + 18f * h1) * spread;

                GraphicsState saved = g.Save();
                try
                {
                    g.TranslateTransform(c.X * w + ox, c.Y * h + oy);
                    g.RotateTransform(roll);
                    g.TranslateTransform(-c.X * w, -c.Y * h);

                    using (var cellPath = ScalePoly(poly, w, h))
                    {
                        g.SetClip(cellPath, CombineMode.Intersect);
                        float frostMix = inv * 0.78f;
                        Color col = MixInk(ink, iceDeep, frostMix, arriveOpacity);
                        FillGlyph(g, next, col);

                        if (inv > 0.05f)
                        {
                            g.SetClip(next, CombineMode.Intersect);
                            using (var rim = new Pen(Color.FromArgb((int)(200 * inv), iceBright), Math.Max(0.9f, h * 0.018f)))
                                g.DrawPath(rim, cellPath);
                        }
                    }
                }
                finally { g.Restore(saved); }
            }
        }
    }

    // =========================================================================
    // 3. SPLIT FLAP: True 3D perspective trapezoidal foreshortening, visible flap
    //    thickness lip, Lambert angle-based lighting, sweeping specular sheen,
    //    dynamic cast shadows, and two-stage damped mechanical gravity bounce.
    // =========================================================================
    private void PaintFlap3D(Graphics g, GraphicsPath oldFace, GraphicsPath next, Color ink, float w, float h, float t)
    {
        float cardW = w * 0.96f;
        float half = h * 0.52f;
        float cardH = half * 2f;
        float radius = Math.Max(3.5f, h * 0.085f);
        bool lightFace = ink.GetBrightness() < 0.5f;

        Color plateTop = lightFace ? Color.FromArgb(245, 248, 250, 253) : Color.FromArgb(242, 30, 38, 52);
        Color plateBot = lightFace ? Color.FromArgb(245, 232, 237, 244) : Color.FromArgb(245, 17, 22, 33);
        Color edgeLip = lightFace ? Color.FromArgb(235, 178, 186, 198) : Color.FromArgb(235, 76, 88, 108);
        Color shadowInk = lightFace ? Color.FromArgb(255, 155, 168, 184) : Color.FromArgb(255, 3, 6, 11);

        // 1. Static physical split-flap housing tile (skipped in PureFloating)
        if (theme != ClockTheme.PureFloating)
        {
            RectangleF cardBounds = new RectangleF(-cardW * 0.5f, -half, cardW, cardH);
            using (var cardPath = CreateRoundedRect(cardBounds, radius))
            {
                GraphicsState sh = g.Save();
                g.TranslateTransform(0f, 2.2f);
                using (var sb = new SolidBrush(Color.FromArgb(lightFace ? 34 : 88, 0, 0, 0)))
                    g.FillPath(sb, cardPath);
                g.Restore(sh);

                using (var plateBrush = new LinearGradientBrush(cardBounds, plateTop, plateBot, LinearGradientMode.Vertical))
                    g.FillPath(plateBrush, cardPath);

                using (var rim = new Pen(Color.FromArgb(lightFace ? 135 : 52, 255, 255, 255), 1.0f))
                    g.DrawPath(rim, cardPath);
            }
        }

        if (t <= 0f || t >= 1f)
        {
            GraphicsState rest = g.Save();
            try
            {
                if (theme != ClockTheme.PureFloating) g.ScaleTransform(0.88f, 0.88f);
                PaintRestingGlyph(g, t <= 0f ? oldFace : next, ink);
            }
            finally { g.Restore(rest); }
        }
        else
        {
            // Gravity-accelerated mechanical drop + realistic two-stage damped bounce on landing
            float flipProgress;
            if (t < 0.78f)
            {
                float u = t / 0.78f;
                flipProgress = (float)(u < 0.45f
                    ? (u / 0.45f) * (u / 0.45f) * 0.45f
                    : 0.45f + (1.0 - Math.Pow(1.0 - (u - 0.45f) / 0.55f, 2.6)) * 0.55f);
            }
            else
            {
                float b = (t - 0.78f) / 0.22f;
                // Damped double rebound (~3.5% then ~1.1%)
                flipProgress = 1f - (float)(Math.Abs(Math.Sin(b * Math.PI * 1.5)) * 0.045 * Math.Pow(1f - b, 1.35));
            }

            float angle = flipProgress * (float)Math.PI;
            float cosA = (float)Math.Cos(angle);
            float sinA = (float)Math.Sin(angle); // 0 at flat, 1 when jutting straight toward viewer!

            if (cosA >= 0f)
            {
                // FIRST HALF: Upper flap of oldFace swings forward & down toward the hinge
                // Static revealed upper half of next (darkened by dynamic cast shadow of the overhanging flap)
                float castAmt = 0.52f * cosA;
                FlapSlice(g, next, MixInk(ink, shadowInk, castAmt * 0.62f, 1f), w, -half, 0f, 1f);
                if (theme != ClockTheme.PureFloating && castAmt > 0.02f)
                {
                    float shadowH = half * cosA;
                    using (var cast = new LinearGradientBrush(
                        new RectangleF(-cardW * 0.49f, -shadowH, cardW * 0.98f, Math.Max(1f, shadowH)),
                        Color.FromArgb((int)(125 * castAmt), 0, 0, 0),
                        Color.FromArgb((int)(35 * castAmt), 0, 0, 0),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(cast, -cardW * 0.49f, -shadowH, cardW * 0.98f, shadowH);
                    }
                }

                // Static lower half of oldFace
                FlapSlice(g, oldFace, ink, w, 0f, half, 1f);

                // 3D perspective trapezoidal swinging upper flap!
                float topY = -half * cosA;
                float flare = 1f + 0.13f * sinA; // outer edge widens toward the viewer (true perspective!)
                float shade = 1f - cosA;

                if (theme != ClockTheme.PureFloating)
                {
                    // Visible 3D card thickness lip on the outer swinging edge
                    float lipH = Math.Max(1.2f, 3.2f * sinA);
                    PointF[] lipPoly =
                    {
                        new PointF(-cardW * 0.5f * flare, topY - lipH),
                        new PointF(cardW * 0.5f * flare, topY - lipH),
                        new PointF(cardW * 0.5f * flare, topY),
                        new PointF(-cardW * 0.5f * flare, topY)
                    };
                    using (var lb = new SolidBrush(edgeLip))
                        g.FillPolygon(lb, lipPoly);

                    // Perspective trapezoid flap card body
                    PointF[] trap =
                    {
                        new PointF(-cardW * 0.5f * flare, topY),
                        new PointF(cardW * 0.5f * flare, topY),
                        new PointF(cardW * 0.5f, 0f),
                        new PointF(-cardW * 0.5f, 0f)
                    };
                    using (var fb = new SolidBrush(MixInk(plateTop, shadowInk, shade * 0.68f, 0.98f)))
                        g.FillPolygon(fb, trap);
                }

                // Project glyph smoothly inside the 3D perspective flap
                FlapSlice3D(g, oldFace, MixInk(ink, shadowInk, shade * 0.58f, 1f), w, topY, 0f, cosA, flare, true);
            }
            else
            {
                // SECOND HALF: Lower flap of next swings down from the hinge and snaps onto the bottom half
                float k = -cosA;
                float botY = half * k;
                float flare = 1f + 0.13f * sinA;

                // Static upper half of next
                FlapSlice(g, next, ink, w, -half, 0f, 1f);

                // Static lower half of oldFace with soft advancing shadow
                float underShadow = 0.54f * (1f - k);
                FlapSlice(g, oldFace, MixInk(ink, shadowInk, underShadow * 0.52f, 1f), w, botY, half, 1f);
                if (theme != ClockTheme.PureFloating && k < 0.98f)
                {
                    float shH = Math.Min(half - botY, half * 0.42f * (1f - k) + 3.5f);
                    using (var cast = new LinearGradientBrush(
                        new RectangleF(-cardW * 0.49f, botY, cardW * 0.98f, Math.Max(1f, shH)),
                        Color.FromArgb((int)(130 * (1f - k * 0.55f)), 0, 0, 0),
                        Color.FromArgb(0, 0, 0, 0),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(cast, -cardW * 0.49f, botY, cardW * 0.98f, shH);
                    }
                }

                // 3D perspective trapezoidal sweeping lower flap with specular angle catch-light
                float glint = (float)Math.Pow(Math.Sin(k * Math.PI), 1.6) * 0.38f;
                if (theme != ClockTheme.PureFloating)
                {
                    PointF[] trap =
                    {
                        new PointF(-cardW * 0.5f, 0f),
                        new PointF(cardW * 0.5f, 0f),
                        new PointF(cardW * 0.5f * flare, botY),
                        new PointF(-cardW * 0.5f * flare, botY)
                    };
                    Color flapBg = MixInk(plateBot, lightFace ? Color.White : Color.FromArgb(255, 96, 118, 152), glint, 0.98f);
                    using (var fb = new SolidBrush(flapBg))
                        g.FillPolygon(fb, trap);

                    // Visible 3D card thickness lip on the bottom swinging edge
                    float lipH = Math.Max(1.2f, 3.2f * sinA);
                    PointF[] lipPoly =
                    {
                        new PointF(-cardW * 0.5f * flare, botY),
                        new PointF(cardW * 0.5f * flare, botY),
                        new PointF(cardW * 0.5f * flare, botY + lipH),
                        new PointF(-cardW * 0.5f * flare, botY + lipH)
                    };
                    using (var lb = new SolidBrush(edgeLip))
                        g.FillPolygon(lb, lipPoly);
                }

                Color flapInk = MixInk(ink, shadowInk, (1f - k) * 0.30f, 1f);
                FlapSlice3D(g, next, flapInk, w, 0f, botY, k, flare, false);
            }
        }

        // 2. Crisp mechanical center hinge slot, catch-light lip, and metallic side hinge barrels
        GraphicsState saved = g.Save();
        try
        {
            float thickness = Math.Max(1.8f, h * 0.028f);
            Color seamColor = lightFace ? Color.FromArgb(200, 58, 68, 84) : Color.FromArgb(228, 4, 8, 14);

            if (theme == ClockTheme.PureFloating)
            {
                g.SetClip(new RectangleF(-w * 0.5f, -thickness * 0.5f, w, thickness), CombineMode.Intersect);
                FillGlyph(g, t < 0.5f ? oldFace : next, seamColor);
            }
            else
            {
                using (var hinge = new Pen(seamColor, thickness))
                    g.DrawLine(hinge, -cardW * 0.49f, 0f, cardW * 0.49f, 0f);

                using (var lip = new Pen(Color.FromArgb(lightFace ? 120 : 40, 255, 255, 255), 0.9f))
                    g.DrawLine(lip, -cardW * 0.45f, thickness * 0.65f, cardW * 0.45f, thickness * 0.65f);

                // Brushed metallic left & right hinge barrels
                float pinW = Math.Max(2.6f, cardW * 0.055f);
                float pinH = Math.Max(4.8f, h * 0.088f);
                Color pinCol = lightFace ? Color.FromArgb(210, 128, 138, 152) : Color.FromArgb(220, 62, 74, 94);
                using (var pinBrush = new SolidBrush(pinCol))
                {
                    g.FillRectangle(pinBrush, -cardW * 0.50f, -pinH * 0.5f, pinW, pinH);
                    g.FillRectangle(pinBrush, cardW * 0.50f - pinW, -pinH * 0.5f, pinW, pinH);
                }
            }
        }
        finally { g.Restore(saved); }
    }

    private void FlapSlice(Graphics g, GraphicsPath face, Color color, float w, float top, float bottom, float scaleY)
    {
        if (bottom <= top || scaleY < 0.004f) return;
        GraphicsState saved = g.Save();
        try
        {
            g.SetClip(new RectangleF(-w * 0.54f, top, w * 1.08f, bottom - top), CombineMode.Intersect);
            float fit = theme == ClockTheme.PureFloating ? 1f : 0.88f;
            g.ScaleTransform(fit, scaleY * fit);
            FillGlyph(g, face, color);
        }
        finally { g.Restore(saved); }
    }

    // Renders the glyph inside a 3D perspective flap by slightly flaring horizontal scale proportionally to distance from hinge
    private void FlapSlice3D(Graphics g, GraphicsPath face, Color color, float w, float top, float bottom,
        float scaleY, float flare, bool isUpper)
    {
        if (bottom <= top || scaleY < 0.006f) return;
        GraphicsState saved = g.Save();
        try
        {
            float fit = theme == ClockTheme.PureFloating ? 1f : 0.88f;
            float avgScaleX = fit * (1f + (flare - 1f) * 0.52f);
            g.SetClip(new RectangleF(-w * 0.56f * flare, top, w * 1.12f * flare, bottom - top), CombineMode.Intersect);
            g.ScaleTransform(avgScaleX, scaleY * fit);
            FillGlyph(g, face, color);
        }
        finally { g.Restore(saved); }
    }

    private static GraphicsPath ScalePoly(PointF[] poly, float w, float h)
    {
        PointF[] scaled = new PointF[poly.Length];
        for (int i = 0; i < poly.Length; i++)
            scaled[i] = new PointF(poly[i].X * w, poly[i].Y * h);
        var path = new GraphicsPath();
        path.AddPolygon(scaled);
        return path;
    }

    private static PointF PolyCentroid(PointF[] poly)
    {
        float x = 0f, y = 0f;
        for (int i = 0; i < poly.Length; i++)
        {
            x += poly[i].X;
            y += poly[i].Y;
        }
        return new PointF(x / poly.Length, y / poly.Length);
    }
}
