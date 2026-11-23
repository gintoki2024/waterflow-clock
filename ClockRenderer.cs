using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

internal sealed class ClockRenderer : IDisposable
{
    internal const int Duration = 880;
    private const double CascadeStepMs = 52.0;
    private const double ShimmerTailMs = 200.0;

    private readonly ClockTheme theme;
    private readonly ClockFlowStyle flowStyle;
    private readonly bool isHandwritten;
    private readonly GraphicsPath[] glyphs = new GraphicsPath[10];
    private readonly Digit[] digits = new Digit[8];
    private readonly float[] centers = new float[8];
    private readonly Stopwatch timer = Stopwatch.StartNew();

    private readonly float digitHeight;
    private readonly float digitWidth;
    private readonly float rowHeight;
    private readonly float centerY;
    private readonly RectangleF cardRect;
    private readonly float cornerRadius;

    private readonly byte[] rowMask;
    private readonly byte[] digitPixels;
    private readonly byte[] outputPixels;
    private byte[] cachedBgPixels;
    private Point cachedOrigin = new Point(int.MinValue, int.MinValue);
    private int cachedWallpaperVersion = -1;

    private readonly Bitmap digitLayer;

    public Size Size { get; private set; }
    public Size PresetSize { get; private set; }
    public bool AutoColor { get; set; }
    private bool hasPalette, useDarkInk;
    private double nextSample;
    private Point sampledOrigin;
    private readonly int[] sampleX;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint operation);

    private sealed class Digit
    {
        public char Value;
        public char OldValue;
        public double Changed;
    }

    public ClockRenderer(string fontName, ClockTheme theme, ClockScale scale, ClockFlowStyle flowStyle, int zoom = 10000)
    {
        this.theme = theme;
        this.flowStyle = flowStyle;
        this.isHandwritten = string.Equals(fontName, HandwrittenDigits.BuiltInFontName, StringComparison.Ordinal);
        float em = GetEmSize(scale);

        if (isHandwritten)
        {
            float targetH = em * 0.84f;
            float maxWidth = 0f;
            float maxHeight = 0f;
            for (int i = 0; i < 10; i++)
            {
                glyphs[i] = HandwrittenDigits.BuildDigit(i, targetH);
                RectangleF b = glyphs[i].GetBounds();
                if (b.Width > maxWidth) maxWidth = b.Width;
                if (b.Height > maxHeight) maxHeight = b.Height;
            }
            digitHeight = Math.Max(24f, maxHeight);
            digitWidth = Math.Max(16f, maxWidth);
        }
        else
        {
            FontFamily family = ResolveFontFamily(fontName);
            using (family)
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                FontStyle style = FontStyle.Regular;
                if (!family.IsStyleAvailable(style))
                    style = family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold :
                        family.IsStyleAvailable(FontStyle.Italic) ? FontStyle.Italic : FontStyle.Bold | FontStyle.Italic;

                float maxWidth = 0f;
                float top = float.MaxValue;
                float bottom = float.MinValue;

                for (int i = 0; i < 10; i++)
                {
                    GraphicsPath path = new GraphicsPath(FillMode.Winding);
                    path.AddString(i.ToString(CultureInfo.InvariantCulture), family, (int)style, em, PointF.Empty, format);
                    glyphs[i] = path;
                    RectangleF b = path.GetBounds();
                    if (b.Width > maxWidth) maxWidth = b.Width;
                    if (b.Top < top) top = b.Top;
                    if (b.Bottom > bottom) bottom = b.Bottom;
                }

                digitHeight = Math.Max(24f, bottom - top);
                digitWidth = Math.Max(16f, maxWidth);
                float midY = (top + bottom) * 0.5f;

                for (int i = 0; i < 10; i++)
                {
                    RectangleF b = glyphs[i].GetBounds();
                    using (Matrix m = new Matrix())
                    {
                        m.Translate(-(b.Left + b.Width * 0.5f), -midY);
                        glyphs[i].Transform(m);
                    }
                }
            }
        }

        // Vertical geometry: exact 1-digit-height above + 1-digit-height center + 1-digit-height below
        rowHeight = (float)Math.Ceiling(digitHeight * 1.08f);
        float shadowMargin = theme == ClockTheme.PureFloating ? 12f : 20f;
        float cardPadY = theme == ClockTheme.PureFloating ? 4f : Math.Max(10f, rowHeight * 0.14f);
        float cardPadX = theme == ClockTheme.PureFloating ? 16f : Math.Max(28f, digitWidth * 0.65f);

        float digitSlot = digitWidth * (isHandwritten ? 1.12f : 1.18f);
        float colonSlot = digitWidth * 0.64f;

        float innerWidth = digitSlot * 6f + colonSlot * 2f;
        float cardW = innerWidth + cardPadX * 2f;
        float cardH = rowHeight * 3f + cardPadY * 2f;

        cardRect = new RectangleF(shadowMargin, shadowMargin, cardW, cardH);
        cornerRadius = Math.Min(28f, cardH * 0.14f);
        centerY = cardRect.Top + cardPadY + rowHeight * 1.5f;

        float x = cardRect.Left + cardPadX;
        for (int i = 0; i < 8; i++)
        {
            float slot = (i == 2 || i == 5) ? colonSlot : digitSlot;
            centers[i] = x + slot * 0.5f;
            x += slot;
            digits[i] = new Digit();
        }

        PresetSize = new Size(
            (int)Math.Ceiling(cardW + shadowMargin * 2f),
            (int)Math.Ceiling(cardH + shadowMargin * 2f));
        zoom = Options.ClampZoom(zoom);
        Size = SizeAtZoom(PresetSize, zoom);
        if (zoom != 10000)
        {
            float factor = zoom / 10000f;
            using (Matrix transform = new Matrix())
            {
                transform.Scale(factor, factor);
                foreach (GraphicsPath glyph in glyphs) glyph.Transform(transform);
            }
            for (int i = 0; i < centers.Length; i++) centers[i] *= factor;
            digitWidth *= factor;
            digitHeight *= factor;
            rowHeight *= factor;
            centerY *= factor;
            cornerRadius *= factor;
            cardRect = new RectangleF(cardRect.X * factor, cardRect.Y * factor, cardRect.Width * factor, cardRect.Height * factor);
        }

        int pixelBytes = Size.Width * Size.Height * 4;
        // Digit-pair gaps and colon midpoints stay clear throughout the vertical animations.
        sampleX = new int[] { (int)((centers[0] + centers[1]) / 2), (int)centers[2],
            (int)((centers[3] + centers[4]) / 2), (int)centers[5], (int)((centers[6] + centers[7]) / 2) };
        digitPixels = new byte[pixelBytes];
        outputPixels = new byte[pixelBytes];
        digitLayer = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppPArgb);

        // Precompute smooth 1D vertical fade mask across the 3-row aperture
        rowMask = new byte[Size.Height];
        float solidHalf = rowHeight * 0.48f;
        float fadeSpan = rowHeight * 1.00f;
        for (int y = 0; y < Size.Height; y++)
        {
            float dist = Math.Abs((y + 0.5f) - centerY);
            if (dist <= solidHalf)
            {
                rowMask[y] = 255;
            }
            else if (dist >= solidHalf + fadeSpan)
            {
                rowMask[y] = 0;
            }
            else
            {
                double u = (dist - solidHalf) / fadeSpan;
                double vis = 0.5 * (1.0 + Math.Cos(Math.PI * Math.Pow(u, 0.88)));
                rowMask[y] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(vis * 255.0)));
            }
        }
    }

    private static float GetEmSize(ClockScale scale)
    {
        switch (scale)
        {
            case ClockScale.Compact: return 62f;
            case ClockScale.Large: return 106f;
            default: return 82f;
        }
    }

    internal static Size SizeAtZoom(Size preset, int zoom)
    {
        double factor = Options.ClampZoom(zoom) / 10000.0;
        return new Size((int)Math.Round(preset.Width * factor), (int)Math.Round(preset.Height * factor));
    }

    private static FontFamily ResolveFontFamily(string preferred)
    {
        string[] fallbacks = { preferred, "Bahnschrift SemiLight", "Bahnschrift", "Segoe UI Light", "Segoe UI" };
        foreach (string name in fallbacks)
        {
            if (string.IsNullOrEmpty(name)) continue;
            try { return new FontFamily(name); }
            catch (ArgumentException) { }
        }
        return FontFamily.GenericSansSerif;
    }

    public Bitmap Render(DateTime time, bool twelve, Point screenOrigin, Wallpaper wallpaper)
    {
        EnsureBackgroundCache(screenOrigin, wallpaper);

        string value = time.ToString(twelve ? "hh:mm:ss" : "HH:mm:ss", CultureInfo.InvariantCulture);
        double now = timer.Elapsed.TotalMilliseconds;
        if (AutoColor) UpdatePalette(screenOrigin, wallpaper, now);
        else hasPalette = false;

        // Effect #4: Detect changes from right to left and assign staggered cascade wave delays
        int cascadeRank = 0;
        for (int index = 7; index >= 0; index--)
        {
            if (index == 2 || index == 5) continue;
            Digit digit = digits[index];
            char targetChar = value[index];
            if (digit.Value == 0)
            {
                digit.Value = targetChar;
                digit.OldValue = '\0';
                digit.Changed = now - Duration - ShimmerTailMs - 10;
            }
            else if (digit.Value != targetChar)
            {
                digit.OldValue = digit.Value;
                digit.Value = targetChar;
                if (flowStyle == ClockFlowStyle.LiquidStream && index == 7)
                {
                    digit.Changed = now;
                }
                else
                {
                    digit.Changed = now + cascadeRank * CascadeStepMs;
                    cascadeRank++;
                }
            }
        }

        using (Graphics g = Graphics.FromImage(digitLayer))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color primaryColor = GetPrimaryDigitColor();
            Color secondsColor = GetSecondsDigitColor();
            Color colonColor = GetColonColor();
            bool drawDropShadow = (theme == ClockTheme.PureFloating);
            bool enableShimmer = (flowStyle != ClockFlowStyle.MinimalGlide);

            // Smooth breathing colon synced to each second
            double phase = (time.Millisecond / 1000.0) * Math.PI * 2.0;
            float colonAlpha = (float)(0.52 + 0.43 * (0.5 + 0.5 * Math.Cos(phase)));

            for (int index = 0; index < 8; index++)
            {
                if (index == 2 || index == 5)
                {
                    PaintColon(g, centers[index], centerY, colonColor, colonAlpha, drawDropShadow);
                    continue;
                }

                Color ink = (index >= 6) ? secondsColor : primaryColor;
                float x = centers[index];

                // Effect #1: Continuous liquid stream on seconds ones-digit in LiquidStream mode
                if (flowStyle == ClockFlowStyle.LiquidStream && index == 7)
                {
                    RenderContinuousSecondsStream(g, value[7] - '0', time.Millisecond / 1000.0, x, ink, drawDropShadow);
                    continue;
                }

                Digit digit = digits[index];
                double elapsed = now - digit.Changed;

                // Waiting for right-to-left cascade stagger
                if (digit.OldValue != 0 && elapsed < 0)
                {
                    PaintDigit(g, digit.OldValue, x, centerY, 1.0f, 1.0f, ink, 1.0f, drawDropShadow, -1f);
                    continue;
                }

                double t = Math.Min(1.0, Math.Max(0.0, elapsed / Duration));

                if (digit.OldValue != 0 && t < 1.0)
                {
                    float ease = ComputeEase(t);
                    float travel = rowHeight * 1.08f;

                    // Optional buoyant micro-overshoot in PrecisionCylinder mode
                    float overshootPx = 0f;
                    if (flowStyle == ClockFlowStyle.PrecisionCylinder && t > 0.52)
                    {
                        double bt = (t - 0.52) / 0.48;
                        overshootPx = (float)(Math.Sin(bt * Math.PI) * (1.0 - bt) * rowHeight * 0.045);
                    }

                    // Departing digit glides UP into top reserved space
                    float oldY = centerY - travel * ease;
                    float oldSx, oldSy;
                    ComputePerspective(oldY, out oldSx, out oldSy);
                    float oldAlpha = (float)Math.Pow(1.0 - ease, 0.52);
                    PaintDigit(g, digit.OldValue, x, oldY, oldSx, oldSy, ink, oldAlpha, drawDropShadow, -1f);

                    // Arriving digit rises UP from bottom reserved space into center
                    float newY = centerY + travel * (1.0f - ease) - overshootPx;
                    float newSx, newSy;
                    ComputePerspective(newY, out newSx, out newSy);
                    float newAlpha = (float)Math.Pow(ease, 0.44);

                    // Effect #5: Arrival specular shimmer starting in the final 30% of glide
                    float shimmer = enableShimmer ? ComputeShimmerProgress(elapsed) : -1f;
                    PaintDigit(g, digit.Value, x, newY, newSx, newSy, ink, newAlpha, drawDropShadow, shimmer);
                }
                else
                {
                    float shimmer = (enableShimmer && digit.OldValue != 0) ? ComputeShimmerProgress(elapsed) : -1f;
                    PaintDigit(g, digit.Value, x, centerY, 1.0f, 1.0f, ink, 1.0f, drawDropShadow, shimmer);
                }
            }
        }

        // Composite digit layer onto cached background (with Effect #2 waterline refraction + bokeh dissolve)
        Rectangle rect = new Rectangle(Point.Empty, Size);
        BitmapData srcData = digitLayer.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            Marshal.Copy(srcData.Scan0, digitPixels, 0, digitPixels.Length);
        }
        finally
        {
            digitLayer.UnlockBits(srcData);
        }

        CompositeFrame(now);

        Bitmap frame = new Bitmap(Size.Width, Size.Height, PixelFormat.Format32bppPArgb);
        try
        {
            BitmapData dstData = frame.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(outputPixels, 0, dstData.Scan0, outputPixels.Length); }
            finally { frame.UnlockBits(dstData); }
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    private void RenderContinuousSecondsStream(Graphics g, int currDigit, double frac, float x, Color ink, bool dropShadow)
    {
        // C1-continuous river curve: lingers gently near center (0.40x speed), flows smoothly between digits (1.60x speed)
        double p = frac - 0.095 * Math.Sin(2.0 * Math.PI * frac);
        float stepY = rowHeight * 1.06f;

        int[] seq =
        {
            (currDigit + 9) % 10, // prev (dissolving in upper zone)
            currDigit,            // current (crossing center)
            (currDigit + 1) % 10, // next (rising from lower zone)
            (currDigit + 2) % 10  // approaching tail
        };

        for (int i = 0; i < seq.Length; i++)
        {
            float y = centerY + (i - 1 - (float)p) * stepY;
            if (Math.Abs(y - centerY) > rowHeight * 1.65f) continue;

            float sx, sy;
            ComputePerspective(y, out sx, out sy);

            // Subtle distance attenuation on top of per-pixel vertical mask
            double distNorm = Math.Min(1.0, Math.Abs(y - centerY) / (rowHeight * 1.45));
            float alpha = (float)(1.0 - 0.28 * distNorm * distNorm);

            // Gentle water-glint shimmer right as the current digit settles through center
            float shimmer = (i == 1 && frac < 0.28) ? (float)(frac / 0.28) : -1f;
            PaintDigit(g, (char)('0' + seq[i]), x, y, sx, sy, ink, alpha, dropShadow, shimmer);
        }
    }

    private float ComputeEase(double t)
    {
        if (flowStyle == ClockFlowStyle.PrecisionCylinder)
        {
            // Crisp mechanical acceleration with silky deceleration
            return (float)(1.0 - Math.Pow(1.0 - t, 3.2));
        }
        // Calm liquid sine-in-out blended with gentle deceleration
        double sineEase = 0.5 - 0.5 * Math.Cos(Math.PI * t);
        double decelEase = 1.0 - Math.Pow(1.0 - t, 2.2);
        return (float)(sineEase * 0.68 + decelEase * 0.32);
    }

    // Effect #3: 3D cylindrical roller perspective
    private void ComputePerspective(float y, out float scaleX, out float scaleY)
    {
        double norm = Math.Min(1.0, Math.Abs(y - centerY) / (rowHeight * 1.48));
        double theta = norm * (Math.PI * 0.45);
        double cosT = Math.Cos(theta);

        if (flowStyle == ClockFlowStyle.PrecisionCylinder)
        {
            scaleY = (float)Math.Max(0.28, cosT);
            scaleX = 1.0f - 0.07f * (1.0f - scaleY);
        }
        else if (flowStyle == ClockFlowStyle.LiquidStream)
        {
            scaleY = (float)(1.0 - 0.28 * (1.0 - cosT));
            scaleX = 1.0f - 0.04f * (1.0f - scaleY);
        }
        else
        {
            float s = 1.0f - 0.06f * (float)(norm * norm);
            scaleX = s;
            scaleY = s;
        }
    }

    private static float ComputeShimmerProgress(double elapsed)
    {
        double start = Duration * 0.68;
        double span = Duration * 0.32 + ShimmerTailMs;
        if (elapsed < start || elapsed > start + span) return -1f;
        return (float)((elapsed - start) / span);
    }

    private void CompositeFrame(double nowMs)
    {
        int w = Size.Width;
        int h = Size.Height;
        bool useRipple = (flowStyle == ClockFlowStyle.LiquidStream || flowStyle == ClockFlowStyle.CalmRipple);

        if (cachedBgPixels == null)
            Array.Clear(outputPixels, 0, outputPixels.Length);
        else
            Buffer.BlockCopy(cachedBgPixels, 0, outputPixels, 0, outputPixels.Length);

        for (int y = 0, rowOffset = 0; y < h; y++, rowOffset += w * 4)
        {
            int m = rowMask[y];
            if (m == 0) continue;

            bool rippleRow = useRipple && m < 255;
            double waveShift = 0.0;
            float bokehSpread = 0f;
            if (rippleRow)
            {
                // Effect #2: Gentle flowing waterline refraction + soft bokeh diffusion
                double depth = (255 - m) / 255.0;
                waveShift = Math.Sin(y * 0.17 - nowMs * 0.0042) * (depth * depth) * 3.8;
                bokehSpread = 0.55f + (float)(depth * 2.2);
            }

            for (int x = 0, idx = rowOffset; x < w; x++, idx += 4)
            {
                int sb, sg, sr, sa;
                if (!rippleRow)
                {
                    int rawA = digitPixels[idx + 3];
                    if (rawA == 0) continue;
                    if (m == 255)
                    {
                        sb = digitPixels[idx];
                        sg = digitPixels[idx + 1];
                        sr = digitPixels[idx + 2];
                        sa = rawA;
                    }
                    else
                    {
                        sb = (digitPixels[idx] * m + 127) / 255;
                        sg = (digitPixels[idx + 1] * m + 127) / 255;
                        sr = (digitPixels[idx + 2] * m + 127) / 255;
                        sa = (rawA * m + 127) / 255;
                    }
                }
                else
                {
                    // 3-tap horizontal liquid blur + wave refraction
                    float cx = (float)(x + waveShift);
                    int b0, g0, r0, a0, b1, g1, r1, a1, b2, g2, r2, a2;
                    SampleRowLinear(rowOffset, w, cx - bokehSpread, out b0, out g0, out r0, out a0);
                    SampleRowLinear(rowOffset, w, cx, out b1, out g1, out r1, out a1);
                    SampleRowLinear(rowOffset, w, cx + bokehSpread, out b2, out g2, out r2, out a2);

                    int rawA = (a0 + (a1 << 1) + a2 + 2) >> 2;
                    if (rawA == 0) continue;
                    int rawB = (b0 + (b1 << 1) + b2 + 2) >> 2;
                    int rawG = (g0 + (g1 << 1) + g2 + 2) >> 2;
                    int rawR = (r0 + (r1 << 1) + r2 + 2) >> 2;

                    sb = (rawB * m + 127) / 255;
                    sg = (rawG * m + 127) / 255;
                    sr = (rawR * m + 127) / 255;
                    sa = (rawA * m + 127) / 255;
                }

                if (sa == 0) continue;

                if (cachedBgPixels == null)
                {
                    outputPixels[idx] = (byte)sb;
                    outputPixels[idx + 1] = (byte)sg;
                    outputPixels[idx + 2] = (byte)sr;
                    outputPixels[idx + 3] = (byte)sa;
                }
                else
                {
                    int inv = 255 - sa;
                    outputPixels[idx] = (byte)Math.Min(255, sb + (outputPixels[idx] * inv + 127) / 255);
                    outputPixels[idx + 1] = (byte)Math.Min(255, sg + (outputPixels[idx + 1] * inv + 127) / 255);
                    outputPixels[idx + 2] = (byte)Math.Min(255, sr + (outputPixels[idx + 2] * inv + 127) / 255);
                    outputPixels[idx + 3] = (byte)Math.Min(255, sa + (outputPixels[idx + 3] * inv + 127) / 255);
                }
            }
        }
    }

    private void SampleRowLinear(int rowOffset, int w, float fx, out int b, out int g, out int r, out int a)
    {
        if (fx < 0f || fx >= w - 1)
        {
            int cx = fx < 0f ? 0 : w - 1;
            int i = rowOffset + (cx << 2);
            b = digitPixels[i];
            g = digitPixels[i + 1];
            r = digitPixels[i + 2];
            a = digitPixels[i + 3];
            return;
        }
        int x0 = (int)fx;
        int i0 = rowOffset + (x0 << 2);
        int i1 = i0 + 4;
        int a0 = digitPixels[i0 + 3];
        int a1 = digitPixels[i1 + 3];
        if (a0 == 0 && a1 == 0)
        {
            b = g = r = a = 0;
            return;
        }
        int t = (int)((fx - x0) * 256f);
        int inv = 256 - t;
        b = (digitPixels[i0] * inv + digitPixels[i1] * t) >> 8;
        g = (digitPixels[i0 + 1] * inv + digitPixels[i1 + 1] * t) >> 8;
        r = (digitPixels[i0 + 2] * inv + digitPixels[i1 + 2] * t) >> 8;
        a = (a0 * inv + a1 * t) >> 8;
    }

    private void PaintDigit(Graphics g, char ch, float x, float y, float scaleX, float scaleY,
        Color color, float opacity, bool dropShadow, float shimmerProgress)
    {
        if (opacity <= 0.004f || ch < '0' || ch > '9') return;
        GraphicsPath path = glyphs[ch - '0'];
        int alpha = Math.Max(0, Math.Min(255, (int)Math.Round(color.A * opacity)));
        if (alpha == 0) return;

        GraphicsState state = g.Save();
        try
        {
            if (dropShadow)
            {
                int shadowA = Math.Max(0, Math.Min(255, (int)Math.Round(105 * opacity)));
                g.TranslateTransform(x, y + 3.5f);
                g.ScaleTransform(scaleX, scaleY);
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(shadowA, 8, 12, 20)))
                    g.FillPath(shadowBrush, path);
                g.Restore(state);
                state = g.Save();
            }

            g.TranslateTransform(x, y);
            g.ScaleTransform(scaleX, scaleY);

            if (!dropShadow && theme != ClockTheme.PorcelainLight)
            {
                int glowA = Math.Max(0, Math.Min(255, (int)Math.Round(30 * opacity)));
                using (Pen glowPen = new Pen(Color.FromArgb(glowA, color.R, color.G, color.B), 2.8f))
                {
                    glowPen.LineJoin = LineJoin.Round;
                    g.DrawPath(glowPen, path);
                }
            }

            using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B)))
                g.FillPath(fill, path);

            // Effect #5: Subtle arrival water-glint specular shimmer sweeping across glyph
            if (shimmerProgress >= 0f && shimmerProgress <= 1f)
            {
                float env = (float)Math.Sin(shimmerProgress * Math.PI);
                int peakA = (int)Math.Round((theme == ClockTheme.PorcelainLight ? 95.0 : 88.0) * env * opacity);
                if (peakA > 2)
                {
                    float halfW = digitWidth * 0.75f;
                    float halfH = digitHeight * 0.65f;
                    float sweep = -halfH * 1.6f + shimmerProgress * halfH * 3.2f;
                    PointF p1 = new PointF(-halfW, sweep + halfH * 0.55f);
                    PointF p2 = new PointF(halfW, sweep - halfH * 0.55f);
                    Color zero = Color.FromArgb(0, 255, 255, 255);
                    Color glint = theme == ClockTheme.AbyssalInk
                        ? Color.FromArgb(peakA, 210, 244, 255)
                        : Color.FromArgb(peakA, 255, 255, 255);

                    using (LinearGradientBrush shimmerBrush = new LinearGradientBrush(p1, p2, zero, zero))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Colors = new[] { zero, glint, zero };
                        cb.Positions = new[] { 0f, 0.5f, 1f };
                        shimmerBrush.InterpolationColors = cb;
                        g.FillPath(shimmerBrush, path);
                    }
                }
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void PaintColon(Graphics g, float x, float y, Color color, float opacity, bool dropShadow)
    {
        float r = Math.Max(3.0f, digitHeight * (isHandwritten ? 0.062f : 0.056f));
        float offset = digitHeight * 0.175f;
        int alpha = Math.Max(0, Math.Min(255, (int)Math.Round(color.A * opacity)));

        if (isHandwritten)
        {
            // Organic slightly lopsided hand-dotted ink drops
            Action<Brush, float, float> drawDots = delegate(Brush br, float dx, float dy)
            {
                GraphicsState s1 = g.Save();
                g.TranslateTransform(x + dx + 0.8f, y - offset + dy);
                g.RotateTransform(-14f);
                g.FillEllipse(br, -r * 1.15f, -r * 0.88f, r * 2.3f, r * 1.76f);
                g.Restore(s1);

                GraphicsState s2 = g.Save();
                g.TranslateTransform(x + dx - 1.1f, y + offset + dy + 0.6f);
                g.RotateTransform(11f);
                g.FillEllipse(br, -r * 0.92f, -r * 1.12f, r * 1.84f, r * 2.24f);
                g.Restore(s2);
            };

            if (dropShadow)
            {
                int sa = Math.Max(0, Math.Min(255, (int)Math.Round(95 * opacity)));
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(sa, 8, 12, 20)))
                    drawDots(sb, 0f, 2.5f);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B)))
                drawDots(b, 0f, 0f);
            return;
        }

        if (dropShadow)
        {
            int sa = Math.Max(0, Math.Min(255, (int)Math.Round(95 * opacity)));
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(sa, 8, 12, 20)))
            {
                g.FillEllipse(sb, x - r, y - offset - r + 2.5f, r * 2f, r * 2f);
                g.FillEllipse(sb, x - r, y + offset - r + 2.5f, r * 2f, r * 2f);
            }
        }

        using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B)))
        {
            g.FillEllipse(b, x - r, y - offset - r, r * 2f, r * 2f);
            g.FillEllipse(b, x - r, y + offset - r, r * 2f, r * 2f);
        }
    }

    private Color GetPrimaryDigitColor()
    {
        if (AutoColor) return useDarkInk ? Color.FromArgb(252, 13, 22, 34) : Color.FromArgb(253, 250, 252, 255);
        switch (theme)
        {
            case ClockTheme.PorcelainLight: return Color.FromArgb(245, 22, 24, 30);
            case ClockTheme.AbyssalInk: return Color.FromArgb(248, 241, 246, 255);
            case ClockTheme.PureFloating: return Color.FromArgb(252, 250, 252, 255);
            default: return Color.FromArgb(248, 248, 250, 254);
        }
    }

    private Color GetSecondsDigitColor()
    {
        if (AutoColor) return useDarkInk ? Color.FromArgb(248, 18, 43, 61) : Color.FromArgb(250, 225, 244, 255);
        switch (theme)
        {
            case ClockTheme.PorcelainLight: return Color.FromArgb(215, 58, 72, 92);
            case ClockTheme.AbyssalInk: return Color.FromArgb(245, 130, 214, 255);
            case ClockTheme.PureFloating: return Color.FromArgb(232, 226, 240, 255);
            default: return Color.FromArgb(228, 202, 224, 248);
        }
    }

    private Color GetColonColor()
    {
        if (AutoColor) return useDarkInk ? Color.FromArgb(235, 17, 31, 45) : Color.FromArgb(239, 237, 247, 255);
        switch (theme)
        {
            case ClockTheme.PorcelainLight: return Color.FromArgb(165, 45, 55, 72);
            case ClockTheme.AbyssalInk: return Color.FromArgb(180, 130, 214, 255);
            default: return Color.FromArgb(185, 226, 236, 248);
        }
    }

    private void UpdatePalette(Point origin, Wallpaper wallpaper, double now)
    {
        bool moved = !hasPalette || sampledOrigin != origin;
        if (!moved && now < nextSample) return;
        sampledOrigin = origin;
        nextSample = now + (moved ? 140 : 650);
        int y = (int)centerY;
        // On first paint or move, the last presented card may still be at its previous position.
        using (Bitmap strip = moved ? null : CaptureStrip(origin.X + sampleX[0], origin.Y + y,
            sampleX[sampleX.Length - 1] - sampleX[0] + 1))
        {
            double[] luminance = new double[sampleX.Length];
            for (int i = 0; i < sampleX.Length; i++)
            {
                int x = sampleX[i];
                Color c = strip == null ? wallpaper.Sample(origin.X + x, origin.Y + y) : strip.GetPixel(x - sampleX[0], 0);
                if (strip == null && cachedBgPixels != null)
                {
                    int p = (y * Size.Width + x) * 4;
                    int inv = 255 - cachedBgPixels[p + 3];
                    c = Color.FromArgb(Math.Min(255, cachedBgPixels[p + 2] + c.R * inv / 255),
                        Math.Min(255, cachedBgPixels[p + 1] + c.G * inv / 255),
                        Math.Min(255, cachedBgPixels[p] + c.B * inv / 255));
                }
                luminance[i] = .2126 * LinearChannel(c.R) + .7152 * LinearChannel(c.G) + .0722 * LinearChannel(c.B);
            }
            Array.Sort(luminance);
            double median = luminance[luminance.Length / 2];
            useDarkInk = median >= (!hasPalette ? .19 : useDarkInk ? .16 : .22);
            hasPalette = true;
        }
    }

    private static double LinearChannel(byte v)
    {
        double s = v / 255.0;
        return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
    }

    private static Bitmap CaptureStrip(int x, int y, int width)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        Bitmap capture = null;
        try
        {
            capture = new Bitmap(width, 1, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(capture))
            {
                IntPtr target = g.GetHdc();
                try
                {
                    // SRCCOPY | CAPTUREBLT includes the composed layered clock card.
                    if (!BitBlt(target, 0, 0, width, 1, screen, x, y, 0x40CC0020)) return null;
                }
                finally { g.ReleaseHdc(target); }
            }
            Bitmap result = capture;
            capture = null;
            return result;
        }
        finally
        {
            if (capture != null) capture.Dispose();
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void EnsureBackgroundCache(Point origin, Wallpaper wallpaper)
    {
        if (theme == ClockTheme.PureFloating)
        {
            cachedBgPixels = null;
            return;
        }
        if (cachedBgPixels != null && cachedOrigin == origin && cachedWallpaperVersion == wallpaper.Version)
            return;

        cachedOrigin = origin;
        cachedWallpaperVersion = wallpaper.Version;
        cachedBgPixels = BuildFrostedBackground(origin, wallpaper);
    }

    private byte[] BuildFrostedBackground(Point origin, Wallpaper wallpaper)
    {
        int w = Size.Width;
        int h = Size.Height;
        byte[] bg = new byte[w * h * 4];

        byte[] maskPixels = new byte[w * h * 4];
        using (Bitmap shadowBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
        using (Bitmap maskBmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
        {
            using (Graphics sg = Graphics.FromImage(shadowBmp))
            {
                sg.SmoothingMode = SmoothingMode.HighQuality;
                sg.Clear(Color.Transparent);

                int layers = 6;
                for (int i = layers; i >= 1; i--)
                {
                    float spread = i * 2.4f;
                    float offsetY = 2.0f + i * 1.1f;
                    RectangleF sr = new RectangleF(
                        cardRect.X - spread * 0.55f,
                        cardRect.Y - spread * 0.35f + offsetY,
                        cardRect.Width + spread * 1.1f,
                        cardRect.Height + spread * 0.85f);
                    int a = (int)Math.Round((theme == ClockTheme.PorcelainLight ? 11.0 : 18.0) * (layers - i + 1) / layers);
                    using (GraphicsPath sp = CreateRoundedRect(sr, cornerRadius + spread * 0.35f))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, 0, 4, 12)))
                        sg.FillPath(sb, sp);
                }
            }

            using (Graphics mg = Graphics.FromImage(maskBmp))
            {
                mg.SmoothingMode = SmoothingMode.HighQuality;
                mg.Clear(Color.Transparent);
                using (GraphicsPath cp = CreateRoundedRect(cardRect, cornerRadius))
                using (SolidBrush mb = new SolidBrush(Color.White))
                    mg.FillPath(mb, cp);
            }

            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData sData = shadowBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            BitmapData mData = maskBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                Marshal.Copy(sData.Scan0, bg, 0, bg.Length);
                Marshal.Copy(mData.Scan0, maskPixels, 0, maskPixels.Length);
            }
            finally
            {
                shadowBmp.UnlockBits(sData);
                maskBmp.UnlockBits(mData);
            }
        }

        double veilR, veilG, veilB, veilMix, cardAlpha;
        switch (theme)
        {
            case ClockTheme.PorcelainLight:
                veilR = 250; veilG = 251; veilB = 254;
                veilMix = 0.82; cardAlpha = 0.90;
                break;
            case ClockTheme.AbyssalInk:
                veilR = 11; veilG = 19; veilB = 36;
                veilMix = 0.76; cardAlpha = 0.90;
                break;
            default:
                veilR = 16; veilG = 20; veilB = 28;
                veilMix = 0.62; cardAlpha = 0.86;
                break;
        }

        for (int y = 0; y < h; y++)
        {
            double relY = Math.Max(0.0, Math.Min(1.0, (y - cardRect.Top) / Math.Max(1f, cardRect.Height)));
            double sheen = (1.0 - relY) * (theme == ClockTheme.PorcelainLight ? 10.0 : 18.0);

            int rowOffset = y * w * 4;
            for (int x = 0, idx = rowOffset; x < w; x++, idx += 4)
            {
                int cov = maskPixels[idx + 3];
                if (cov == 0) continue;

                Color wp = wallpaper.Sample(origin.X + x, origin.Y + y);
                double r = Math.Min(255.0, wp.R * (1.0 - veilMix) + veilR * veilMix + sheen);
                double g = Math.Min(255.0, wp.G * (1.0 - veilMix) + veilG * veilMix + sheen);
                double b = Math.Min(255.0, wp.B * (1.0 - veilMix) + veilB * veilMix + sheen * 1.1);

                int sa = (int)Math.Round(cov * cardAlpha);
                int sr = (int)Math.Round(r * sa / 255.0);
                int sg = (int)Math.Round(g * sa / 255.0);
                int sb = (int)Math.Round(b * sa / 255.0);

                int inv = 255 - sa;
                bg[idx] = (byte)Math.Min(255, sb + (bg[idx] * inv + 127) / 255);
                bg[idx + 1] = (byte)Math.Min(255, sg + (bg[idx + 1] * inv + 127) / 255);
                bg[idx + 2] = (byte)Math.Min(255, sr + (bg[idx + 2] * inv + 127) / 255);
                bg[idx + 3] = (byte)Math.Min(255, sa + (bg[idx + 3] * inv + 127) / 255);
            }
        }

        using (Bitmap overlay = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
        {
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData data = overlay.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(bg, 0, data.Scan0, bg.Length); }
            finally { overlay.UnlockBits(data); }

            using (Graphics g = Graphics.FromImage(overlay))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (GraphicsPath clipPath = CreateRoundedRect(cardRect, cornerRadius))
                {
                    g.SetClip(clipPath);

                    float bandTop = centerY - rowHeight * 0.50f;
                    RectangleF bandRect = new RectangleF(cardRect.Left, bandTop, cardRect.Width, rowHeight);
                    Color bandTint = theme == ClockTheme.PorcelainLight
                        ? Color.FromArgb(10, 15, 23, 42)
                        : Color.FromArgb(12, 255, 255, 255);
                    using (SolidBrush bandBrush = new SolidBrush(bandTint))
                        g.FillRectangle(bandBrush, bandRect);

                    DrawFadingHairline(g, cardRect.Left + 18f, cardRect.Right - 18f, bandTop);
                    DrawFadingHairline(g, cardRect.Left + 18f, cardRect.Right - 18f, bandTop + rowHeight);

                    g.ResetClip();

                    RectangleF borderRect = new RectangleF(
                        cardRect.X + 0.6f, cardRect.Y + 0.6f,
                        cardRect.Width - 1.2f, cardRect.Height - 1.2f);
                    using (GraphicsPath borderPath = CreateRoundedRect(borderRect, cornerRadius - 0.6f))
                    using (LinearGradientBrush rimBrush = new LinearGradientBrush(
                        borderRect,
                        theme == ClockTheme.PorcelainLight
                            ? Color.FromArgb(190, 255, 255, 255)
                            : Color.FromArgb(72, 255, 255, 255),
                        theme == ClockTheme.PorcelainLight
                            ? Color.FromArgb(65, 148, 163, 184)
                            : Color.FromArgb(18, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    using (Pen rimPen = new Pen(rimBrush, 1.15f))
                    {
                        g.DrawPath(rimPen, borderPath);
                    }
                }
            }

            BitmapData outData = overlay.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(outData.Scan0, bg, 0, bg.Length); }
            finally { overlay.UnlockBits(outData); }
        }

        return bg;
    }

    private void DrawFadingHairline(Graphics g, float x1, float x2, float y)
    {
        if (x2 <= x1) return;
        Color edge = Color.FromArgb(0, 255, 255, 255);
        Color mid = theme == ClockTheme.PorcelainLight
            ? Color.FromArgb(24, 15, 23, 42)
            : Color.FromArgb(28, 255, 255, 255);

        using (LinearGradientBrush brush = new LinearGradientBrush(
            new PointF(x1, y), new PointF(x2, y), edge, edge))
        {
            ColorBlend cb = new ColorBlend(3);
            cb.Colors = new[] { edge, mid, edge };
            cb.Positions = new[] { 0f, 0.5f, 1f };
            brush.InterpolationColors = cb;
            using (Pen pen = new Pen(brush, 1.0f))
                g.DrawLine(pen, x1, y, x2, y);
        }
    }

    private static GraphicsPath CreateRoundedRect(RectangleF r, float radius)
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

    public void Dispose()
    {
        digitLayer.Dispose();
        foreach (GraphicsPath path in glyphs)
            if (path != null) path.Dispose();
    }
}
