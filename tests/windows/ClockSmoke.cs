using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ClockSmoke
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var wallpaper = new Wallpaper())
        {
            CheckRendering(wallpaper);
            CheckHours(wallpaper);
            CheckResize(wallpaper);
        }
        if (Array.IndexOf(args, "--renderer-only") < 0) CheckWindow();
        Console.WriteLine("Windows clock smoke checks passed.");
    }

    private static void CheckRendering(Wallpaper wallpaper)
    {
        var before = new DateTime(2024, 12, 31, 23, 59, 59, 750);
        foreach (ClockTheme theme in Enum.GetValues(typeof(ClockTheme)))
        foreach (ClockFlowStyle style in Enum.GetValues(typeof(ClockFlowStyle)))
        {
            string face = (int)style % 2 == 0 ? HandwrittenDigits.BuiltInFontName : "Segoe UI";
            ClockScale scale = (ClockScale)(((int)style + (int)theme) % 3);
            using (var renderer = new ClockRenderer(face, theme, scale, style))
            {
                renderer.AutoColor = (int)style % 2 == 0;
                using (var initial = renderer.Render(before, false, Point.Empty, wallpaper))
                {
                    if (initial.Size != renderer.Size || initial.GetPixel(0, 0).A != 0)
                        throw new Exception("Unexpected canvas or opaque outer corner: " + theme);
                    byte[] start = Pixels(initial);
                    int visible = 0;
                    for (int i = 3; i < start.Length; i += 4)
                        if (start[i] != 0) visible++;
                    if (visible < 100) throw new Exception("Empty frame: " + style);
                    DateTime midnight = before.AddMilliseconds(250);
                    using (renderer.Render(midnight, false, Point.Empty, wallpaper)) { }
                    Thread.Sleep(95);
                    using (var moving = renderer.Render(midnight.AddMilliseconds(95), false, Point.Empty, wallpaper))
                        if (Same(start, Pixels(moving))) throw new Exception("Static transition: " + style);
                }
            }
        }
    }

    private static byte[] Still(Wallpaper wallpaper, int hour, bool twelve)
    {
        using (var renderer = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Compact, ClockFlowStyle.MinimalGlide))
        using (var bitmap = renderer.Render(new DateTime(2024, 1, 1, hour, 0, 0), twelve, Point.Empty, wallpaper))
            return Pixels(bitmap);
    }

    private static void CheckResize(Wallpaper wallpaper)
    {
        using (var normal = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Standard, ClockFlowStyle.MinimalGlide))
        {
            Size preset = normal.Size;
            int drag = ClockWindow.ZoomAfterDrag(preset, 10000, preset.Width / 2, preset.Height / 2);
            if (Math.Abs(drag - 15000) > 50 || ClockWindow.ZoomAfterDrag(preset, 15000, 0, 0) != 15000 ||
                ClockWindow.ZoomAfterDrag(preset, 10000, -5000, -5000) != 5000 ||
                ClockWindow.ZoomAfterDrag(preset, 10000, 5000, 5000) != 25000)
                throw new Exception("Unexpected proportional drag or zoom limits.");
            using (var resized = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Standard, ClockFlowStyle.MinimalGlide, drag))
            using (var frame = resized.Render(DateTime.Now, false, Point.Empty, wallpaper))
            {
                if (resized.PresetSize != preset || frame.Size != ClockRenderer.SizeAtZoom(preset, drag) ||
                    Math.Abs(frame.Width / (double)preset.Width - frame.Height / (double)preset.Height) > .005)
                    throw new Exception("Resize preview and final aspect ratio disagree.");
                Rectangle grip = ClockWindow.ResizeGrip(frame.Size);
                ClockWindow.PaintGrip(frame, false);
                if (frame.GetPixel(grip.X + 1, grip.Y + 1).A == 0)
                    throw new Exception("Transparent clock grip cannot receive a click.");
                byte[] before = Pixels(frame);
                ClockWindow.PaintGrip(frame, true);
                if (Same(before, Pixels(frame))) throw new Exception("Resize grip did not appear.");
            }
        }
    }

    private static void CheckHours(Wallpaper wallpaper)
    {
        if (!Same(Still(wallpaper, 0, true), Still(wallpaper, 12, false)) ||
            !Same(Still(wallpaper, 13, true), Still(wallpaper, 1, false)) ||
            Same(Still(wallpaper, 0, false), Still(wallpaper, 12, false)))
            throw new Exception("Incorrect 12- or 24-hour rendering.");
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void CheckWindow()
    {
        using (var clock = new ClockWindow())
        using (var stop = new System.Windows.Forms.Timer { Interval = 1300 })
        {
            bool shown = false;
            clock.Shown += delegate { shown = clock.Visible && clock.IsHandleCreated; stop.Start(); };
            stop.Tick += delegate { stop.Stop(); clock.Close(); };
            Application.Run(clock);
            if (!shown) throw new Exception("Clock window did not open.");
        }
    }
}
