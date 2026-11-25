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
            CheckIllustratedTransitions(wallpaper);
            CheckHours(wallpaper);
            CheckResize(wallpaper);
            CheckCountdown(wallpaper);
            CheckTargetTime();
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

    private static void CheckIllustratedTransitions(Wallpaper wallpaper)
    {
        var before = new DateTime(2025, 1, 1, 12, 34, 58, DateTimeKind.Utc);
        var session = Countdown.Start("Test", 18, before);
        foreach (ClockFlowStyle style in new[] { ClockFlowStyle.Stardust, ClockFlowStyle.IceShards, ClockFlowStyle.SplitFlap })
        foreach (bool countdown in new[] { false, true })
        {
            if (!Enum.IsDefined(typeof(ClockFlowStyle), style))
                throw new Exception("Missing illustrated style " + style);
            using (var renderer = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Compact, style))
            {
                Func<DateTime, Bitmap> frame = t => countdown
                    ? renderer.RenderCountdown(session, t, Point.Empty, wallpaper)
                    : renderer.Render(t, false, Point.Empty, wallpaper);
                DateTime after = before.AddSeconds(1);
                using (var initial = frame(before))
                {
                    using (frame(after)) { }  // Start the monotonic transition at the changed digit.
                    Thread.Sleep(310);
                    using (var moving = frame(after))
                    {
                        Thread.Sleep(1050);
                        using (var settled = frame(after))
                        using (var repeated = frame(after))
                        {
                            int cut = initial.Width * 4 / 5;
                            var ones = new Rectangle(cut, 0, initial.Width - cut, initial.Height);
                            var fixedDigits = new Rectangle(0, 0, initial.Width * 3 / 4, initial.Height);
                            if (!HasEffectColor(style == ClockFlowStyle.SplitFlap ? settled : moving, ones, style))
                                throw new Exception("Missing stardust, ice tint or flap hinge: " + style);
                            if (ChangedPixels(initial, moving, ones) < 10 || ChangedPixels(moving, settled, ones) < 10 ||
                                ChangedPixels(initial, settled, ones) < 10 || ChangedPixels(initial, moving, fixedDigits) != 0 ||
                                !Same(Pixels(settled), Pixels(repeated)))
                                throw new Exception("Incorrect moving or settled frame: " + style + ", countdown=" + countdown);
                        }
                    }
                }
            }
        }
    }

    private static bool HasEffectColor(Bitmap frame, Rectangle area, ClockFlowStyle style)
    {
        int hits = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color c = frame.GetPixel(x, y);
                if (c.A < 45) continue;
                if ((style == ClockFlowStyle.Stardust && (Math.Abs(c.R - c.B) > 25 || Math.Abs(c.G - c.B) > 25)) ||
                    (style == ClockFlowStyle.IceShards && c.B - c.R > 40) ||
                    (style == ClockFlowStyle.SplitFlap && Math.Abs(y - frame.Height * 0.57) <= 6 &&
                     c.A >= 150 && c.R < 160 && c.B < 180))
                    hits++;
            }
        return hits >= 3;
    }

    private static int ChangedPixels(Bitmap a, Bitmap b, Rectangle area)
    {
        int count = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) count++;
        return count;
    }

    private static void CheckTargetTime()
    {
        DateTime now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        DateTime local = new DateTime(2030, 6, 7, 18, 30, 13);
        var target = Countdown.Until("长期目标", local, eastern, now, null, true);
        var restored = Countdown.Restore(target.WithVisibility(false).Store());
        if (restored == null || !restored.IsTarget || restored.Visible || !restored.ShowSecondsOnly ||
            TimeZoneInfo.ConvertTimeFromUtc(restored.Deadline, eastern) != local || restored.Deadline != target.Deadline ||
            restored.Remaining(now) <= 0 || restored.Remaining(target.Deadline.AddTicks(-1)) != 1)
            throw new Exception("Target instant, seconds mode or visibility failed to round-trip.");
        if (Countdown.Format(360000) != "04:00:00" || target.FormatDisplay(360000) != "360000" ||
            Countdown.Format(359999) != "99:59:59" ||
            Countdown.Format(24L * 3600 * 50000 + 3661) != "01:01:01")
            throw new Exception("Day and hour countdown display failed at the 100-hour boundary.");
        string[] old = { "旧倒计时", "300", now.AddMinutes(5).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "1" };
        var migrated = Countdown.Restore(old);
        if (migrated == null || migrated.IsTarget || migrated.ShowSecondsOnly || migrated.Remaining(now) != 300 || !migrated.Visible)
            throw new Exception("Legacy duration record failed to load.");
        DateTime ambiguous = new DateTime(2025, 11, 2, 1, 30, 0);
        // Reject past, >10 years in the future, skipped DST, and ambiguous DST
        foreach (DateTime invalid in new[] { new DateTime(2024, 1, 1), new DateTime(2040, 1, 1), new DateTime(2025, 3, 9, 2, 30, 0), ambiguous })
        {
            try { Countdown.Until("test", invalid, eastern, now, null); }
            catch (ArgumentException) { continue; }
            throw new Exception("Accepted a past, over-10-year, skipped or ambiguous local time.");
        }
        // An existing absolute target can fall within a repeated hour after a timezone change.
        var firstOccurrence = Countdown.Until("test", new DateTime(2025, 11, 2, 5, 30, 0), TimeZoneInfo.Utc, now, null);
        if (Countdown.Until("new name", ambiguous, eastern, now, firstOccurrence).Deadline != firstOccurrence.Deadline)
            throw new Exception("Editing an unchanged ambiguous target altered its absolute instant.");
    }

    private static byte[] Still(Wallpaper wallpaper, int hour, bool twelve)
    {
        using (var renderer = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Compact, ClockFlowStyle.SplitFlap))
        using (var bitmap = renderer.Render(new DateTime(2024, 1, 1, hour, 0, 0), twelve, Point.Empty, wallpaper))
            return Pixels(bitmap);
    }

    private static void CheckResize(Wallpaper wallpaper)
    {
        using (var normal = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Standard, ClockFlowStyle.SplitFlap))
        {
            Size preset = normal.Size;
            int drag = ClockWindow.ZoomAfterDrag(preset, 10000, preset.Width / 2, preset.Height / 2);
            if (Math.Abs(drag - 15000) > 50 || ClockWindow.ZoomAfterDrag(preset, 15000, 0, 0) != 15000 ||
                ClockWindow.ZoomAfterDrag(preset, 10000, -5000, -5000) != 5000 ||
                ClockWindow.ZoomAfterDrag(preset, 10000, 5000, 5000) != 25000)
                throw new Exception("Unexpected proportional drag or zoom limits.");
            using (var resized = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Standard, ClockFlowStyle.SplitFlap, drag))
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

    private static void CheckCountdown(Wallpaper wallpaper)
    {
        var start = new DateTime(2025, 1, 1, 23, 59, 58, DateTimeKind.Utc);
        var session = Countdown.Start("  项目交付  ", Countdown.MaximumSeconds, start);
        var restored = Countdown.Restore(session.Store());
        if (restored == null || restored.Name != "项目交付" || !restored.Visible ||
            restored.Duration != Countdown.MaximumSeconds || restored.Deadline != session.Deadline ||
            restored.Deadline.Kind != DateTimeKind.Utc ||
            Countdown.Format(restored.Remaining(start.AddMilliseconds(999))) != "99:59:59" ||
            Countdown.Format(restored.Remaining(start.AddDays(1))) != "75:59:59" ||
            restored.Remaining(restored.Deadline.AddMilliseconds(-1)) != 1 ||
            restored.Remaining(restored.Deadline.AddDays(1)) != 0 ||
            Countdown.Restore(session.WithVisibility(false).Store()).Visible ||
            Countdown.Restore(new[] { "a", "10", "broken", "1" }) != null)
            throw new Exception("Countdown validation, restoration, rounding or expiry failed.");
        foreach (var input in new[] { new { Name = " ", Seconds = 1 }, new { Name = "工作", Seconds = 0 },
            new { Name = "工作", Seconds = Countdown.MaximumSeconds + 1 } })
        {
            try { Countdown.Start(input.Name, input.Seconds, start); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid countdown accepted.");
        }
        using (var renderer = new ClockRenderer("Segoe UI", ClockTheme.PureFloating, ClockScale.Compact, ClockFlowStyle.LiquidStream))
        using (var active = renderer.RenderCountdown(session, start, Point.Empty, wallpaper))
        using (var renamed = renderer.RenderCountdown(Countdown.Start("读书", session.Duration, start), start, Point.Empty, wallpaper))
        using (var expired = renderer.RenderCountdown(session, session.Deadline, Point.Empty, wallpaper))
        {
            if (Same(Pixels(active), Pixels(renamed)))
                throw new Exception("Changing the countdown name did not change its rendered caption.");
            if (Same(Pixels(active), Pixels(expired)))
                throw new Exception("The active and expired countdown frames are identical.");

            // The ripple phase uses elapsed render time, independently of the supplied UTC time.
            Thread.Sleep(750);
            using (var later = renderer.RenderCountdown(session, session.Deadline.AddDays(5), Point.Empty, wallpaper))
                if (!Same(Pixels(expired), Pixels(later)))
                    throw new Exception("The expired countdown continued animating after reaching zero.");
            using (var clock = renderer.Render(start, false, Point.Empty, wallpaper))
                if (Same(Pixels(clock), Pixels(expired)))
                    throw new Exception("Returning to the clock did not replace the expired countdown.");
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
