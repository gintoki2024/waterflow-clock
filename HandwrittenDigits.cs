using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

internal static class HandwrittenDigits
{
    public const string BuiltInFontName = "随性手写（内置）";

    private struct StrokeDef
    {
        public PointF[] Points;
        public float[] Pressures;
        public float Smoothness; // 0.45..0.55 = silky Catmull-Rom, lower = crisper corners

        public StrokeDef(PointF[] points, float[] pressures, float smoothness = 0.50f)
        {
            Points = points;
            Pressures = pressures;
            Smoothness = smoothness;
        }
    }

    // Authored in a 0..100 x 0..140 canvas with an effortlessly sloppy, carefree ("随便、都行、无所谓") personality.
    public static GraphicsPath BuildDigit(int digit, float targetHeight)
    {
        float scale = targetHeight / 128f;
        float baseRadius = targetHeight * 0.048f;
        GraphicsPath result = new GraphicsPath(FillMode.Winding);

        StrokeDef[] strokes = GetStrokes(digit);
        for (int s = 0; s < strokes.Length; s++)
        {
            AddOrganicStroke(result, strokes[s], scale, baseRadius, digit * 37 + s * 19);
        }

        // Casual individual slouch angle & tiny relaxed baseline bounce
        float[] slouchAngles = { 4.2f, -5.0f, 3.2f, -3.5f, 4.8f, -4.2f, 3.6f, -5.2f, 2.8f, -4.0f };
        float[] yBounce = { 1.2f, -1.8f, 1.5f, -1.0f, 2.0f, -1.4f, 1.6f, -2.0f, 1.0f, -1.5f };

        RectangleF bounds = result.GetBounds();
        using (Matrix m = new Matrix())
        {
            m.Translate(-(bounds.Left + bounds.Width * 0.5f), -(bounds.Top + bounds.Height * 0.5f));
            result.Transform(m);
        }
        using (Matrix rot = new Matrix())
        {
            rot.Rotate(slouchAngles[digit]);
            result.Transform(rot);
        }

        bounds = result.GetBounds();
        using (Matrix m = new Matrix())
        {
            m.Translate(
                -(bounds.Left + bounds.Width * 0.5f),
                -(bounds.Top + bounds.Height * 0.5f) + yBounce[digit] * scale);
            result.Transform(m);
        }

        return result;
    }

    private static StrokeDef[] GetStrokes(int digit)
    {
        switch (digit)
        {
            case 0:
                // Lazily drawn round oval, slightly tilted, closing casually at top
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(58, 14),
                            new PointF(28, 22),
                            new PointF(14, 58),
                            new PointF(18, 102),
                            new PointF(40, 127),
                            new PointF(69, 121),
                            new PointF(85, 86),
                            new PointF(83, 42),
                            new PointF(65, 16),
                            new PointF(45, 20)
                        },
                        new[] { 0.88f, 1.04f, 1.12f, 1.06f, 1.08f, 1.00f, 0.94f, 0.98f, 0.90f, 0.72f },
                        0.52f)
                };

            case 1:
                // Relaxed slightly curved leaning stroke with a carefree little top hook
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(32, 37),
                            new PointF(46, 23),
                            new PointF(56, 14),
                            new PointF(54, 48),
                            new PointF(50, 92),
                            new PointF(48, 128)
                        },
                        new[] { 0.78f, 0.96f, 1.08f, 1.06f, 1.02f, 0.88f },
                        0.36f)
                };

            case 2:
                // Droopy rounded top arch gliding into a slightly wavy, casual upward tail
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(21, 38),
                            new PointF(38, 15),
                            new PointF(67, 16),
                            new PointF(80, 38),
                            new PointF(68, 66),
                            new PointF(40, 96),
                            new PointF(19, 122),
                            new PointF(50, 121),
                            new PointF(84, 115)
                        },
                        new[] { 0.82f, 1.02f, 1.08f, 1.06f, 0.98f, 1.02f, 1.10f, 1.00f, 0.84f },
                        0.44f)
                };

            case 3:
                // Smaller tilted top loop, lazy middle pinch, relaxed rounder bottom belly
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(24, 28),
                            new PointF(50, 13),
                            new PointF(74, 26),
                            new PointF(70, 50),
                            new PointF(46, 65),
                            new PointF(74, 78),
                            new PointF(81, 105),
                            new PointF(58, 127),
                            new PointF(28, 125),
                            new PointF(18, 111)
                        },
                        new[] { 0.80f, 1.02f, 1.08f, 0.96f, 0.86f, 1.04f, 1.12f, 1.06f, 0.96f, 0.78f },
                        0.48f)
                };

            case 4:
                // Carefree open handwritten 4: loose curved L + slouched vertical crossing stem
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(37, 16),
                            new PointF(28, 49),
                            new PointF(17, 86),
                            new PointF(49, 84),
                            new PointF(86, 79)
                        },
                        new[] { 0.86f, 1.04f, 1.10f, 1.00f, 0.84f },
                        0.26f),
                    new StrokeDef(
                        new[]
                        {
                            new PointF(68, 22),
                            new PointF(65, 62),
                            new PointF(61, 102),
                            new PointF(59, 130)
                        },
                        new[] { 0.88f, 1.08f, 1.04f, 0.86f },
                        0.36f)
                };

            case 5:
                // Relaxed drop + round lazy belly, topped with a jaunty tilted hat
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(28, 19),
                            new PointF(25, 44),
                            new PointF(24, 65),
                            new PointF(52, 59),
                            new PointF(78, 74),
                            new PointF(80, 103),
                            new PointF(58, 126),
                            new PointF(29, 126),
                            new PointF(18, 112)
                        },
                        new[] { 0.90f, 1.06f, 1.02f, 0.96f, 1.08f, 1.12f, 1.06f, 0.94f, 0.78f },
                        0.46f),
                    new StrokeDef(
                        new[]
                        {
                            new PointF(28, 21),
                            new PointF(54, 17),
                            new PointF(81, 12)
                        },
                        new[] { 0.92f, 1.04f, 0.82f },
                        0.42f)
                };

            case 6:
                // Slouching smooth back curling into a slightly lopsided round belly
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(68, 14),
                            new PointF(40, 28),
                            new PointF(20, 64),
                            new PointF(18, 103),
                            new PointF(40, 127),
                            new PointF(71, 120),
                            new PointF(82, 93),
                            new PointF(65, 71),
                            new PointF(36, 73),
                            new PointF(21, 92)
                        },
                        new[] { 0.80f, 0.98f, 1.10f, 1.12f, 1.08f, 1.02f, 0.98f, 0.94f, 0.88f, 0.76f },
                        0.52f)
                };

            case 7:
                // Slightly wavy top bar sweeping into a long lazy curved diagonal
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(16, 23),
                            new PointF(48, 18),
                            new PointF(82, 16),
                            new PointF(68, 49),
                            new PointF(51, 89),
                            new PointF(39, 129)
                        },
                        new[] { 0.84f, 1.02f, 1.10f, 1.02f, 1.04f, 0.84f },
                        0.30f)
                };

            case 8:
                // Carefree uneven snowman figure-8 (smaller tilted top, wider bottom)
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(54, 16),
                            new PointF(28, 22),
                            new PointF(24, 45),
                            new PointF(48, 63),
                            new PointF(78, 82),
                            new PointF(81, 109),
                            new PointF(54, 128),
                            new PointF(24, 121),
                            new PointF(19, 94),
                            new PointF(47, 65),
                            new PointF(73, 44),
                            new PointF(71, 22),
                            new PointF(50, 16)
                        },
                        new[] { 0.84f, 1.00f, 1.06f, 0.98f, 1.06f, 1.12f, 1.08f, 1.02f, 0.98f, 0.94f, 0.98f, 0.94f, 0.78f },
                        0.52f)
                };

            default: // 9
                // Slightly squished top loop with a lazy curving stem dropping down-left
                return new[]
                {
                    new StrokeDef(
                        new[]
                        {
                            new PointF(75, 38),
                            new PointF(60, 15),
                            new PointF(32, 18),
                            new PointF(18, 44),
                            new PointF(31, 68),
                            new PointF(59, 66),
                            new PointF(78, 43),
                            new PointF(71, 79),
                            new PointF(56, 111),
                            new PointF(42, 130)
                        },
                        new[] { 0.88f, 1.04f, 1.08f, 1.06f, 1.00f, 0.96f, 1.04f, 1.08f, 1.00f, 0.80f },
                        0.52f)
                };
        }
    }

    // Renders a variable-width smooth spline using overlapping round-capped convex quads + discs
    // so there are NEVER concave cap notches or self-intersection pinholes.
    private static void AddOrganicStroke(GraphicsPath target, StrokeDef def, float scale, float baseRadius, int seed)
    {
        PointF[] pts = def.Points;
        float[] prs = def.Pressures;
        if (pts.Length < 2) return;

        int stepsPerSeg = 8;
        var centerPts = new List<PointF>();
        var radii = new List<float>();

        for (int i = 0; i < pts.Length - 1; i++)
        {
            PointF p0 = pts[Math.Max(0, i - 1)];
            PointF p1 = pts[i];
            PointF p2 = pts[i + 1];
            PointF p3 = pts[Math.Min(pts.Length - 1, i + 2)];

            float pr1 = prs[i];
            float pr2 = prs[i + 1];

            int count = (i == pts.Length - 2) ? stepsPerSeg + 1 : stepsPerSeg;
            for (int step = 0; step < count; step++)
            {
                float t = step / (float)stepsPerSeg;
                PointF pt = EvalCatmullRom(p0, p1, p2, p3, t, def.Smoothness);

                // Subtle hand-drawn organic micro-wobble
                float globalT = (i + t) / (pts.Length - 1);
                float wobbleX = (float)(Math.Sin(globalT * 14.0 + seed) * 0.45 + Math.Cos(globalT * 6.3 + seed * 0.5) * 0.45);
                float wobbleY = (float)(Math.Cos(globalT * 12.5 + seed * 1.3) * 0.45 + Math.Sin(globalT * 5.7 + seed) * 0.45);

                centerPts.Add(new PointF((pt.X + wobbleX) * scale, (pt.Y + wobbleY) * scale));
                float pr = pr1 + (pr2 - pr1) * t;
                radii.Add(baseRadius * pr);
            }
        }

        int n = centerPts.Count;
        if (n < 2) return;

        // Add smooth round start & end caps plus smooth joints as clockwise ellipses (FillMode.Winding union)
        AddDisc(target, centerPts[0], radii[0]);
        for (int i = 0; i < n - 1; i++)
        {
            PointF a = centerPts[i];
            PointF b = centerPts[i + 1];
            float ra = radii[i];
            float rb = radii[i + 1];

            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-3f)
            {
                float nx = -dy / len;
                float ny = dx / len;
                // Clockwise in Y-down screen coordinates (matching GDI+ AddEllipse winding!)
                PointF[] quad =
                {
                    new PointF(a.X - nx * ra, a.Y - ny * ra),
                    new PointF(b.X - nx * rb, b.Y - ny * rb),
                    new PointF(b.X + nx * rb, b.Y + ny * rb),
                    new PointF(a.X + nx * ra, a.Y + ny * ra)
                };
                target.AddPolygon(quad);
            }
            AddDisc(target, b, rb);
        }
    }

    private static void AddDisc(GraphicsPath target, PointF c, float r)
    {
        target.AddEllipse(c.X - r, c.Y - r, r * 2f, r * 2f);
    }

    private static PointF EvalCatmullRom(PointF p0, PointF p1, PointF p2, PointF p3, float t, float s)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float m1x = s * (p2.X - p0.X);
        float m1y = s * (p2.Y - p0.Y);
        float m2x = s * (p3.X - p1.X);
        float m2y = s * (p3.Y - p1.Y);

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return new PointF(
            h00 * p1.X + h10 * m1x + h01 * p2.X + h11 * m2x,
            h00 * p1.Y + h10 * m1y + h01 * p2.Y + h11 * m2y);
    }
}
