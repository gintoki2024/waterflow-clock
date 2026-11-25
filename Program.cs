using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool first;
        using (var instance = new Mutex(true, @"Local\WaterGlassDeskClock", out first))
        {
            if (!first) return;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (var clock = new ClockWindow()) Application.Run(clock); }
            catch (Exception e) { MessageBox.Show(e.Message, "水流时钟", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}

internal sealed class ClockWindow : Form
{
    readonly Options options = Options.Load();
    readonly Wallpaper wallpaper = new Wallpaper();
    ClockRenderer renderer;
    readonly NotifyIcon tray;
    readonly Icon trayIcon;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly ToolStripMenuItem[] themeItems = new ToolStripMenuItem[4];
    readonly ToolStripMenuItem[] flowItems = new ToolStripMenuItem[Enum.GetValues(typeof(ClockFlowStyle)).Length];
    readonly ToolStripMenuItem topMostItem;
    readonly ToolStripMenuItem autoColorItem;
    readonly ToolStripMenuItem countdownMode = new ToolStripMenuItem();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

    bool dragging;
    bool resizing;
    int initialZoom, previewZoom;
    Point dragCursor, dragLocation;
    bool editing;
    long lastRenderedSecond = -1;
    bool lastGripVisible;
    bool lastCountdownVisible;

    public ClockWindow()
    {
        renderer = CreateRenderer();
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = options.TopMost;
        Text = "水流时钟";
        Size = renderer.Size;

        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        Location = options.X == int.MinValue
            ? new Point(work.Right - Width - 40, work.Top + 40)
            : new Point(options.X, options.Y);
        FitScreen();

        // Build clean context menu with instant theme switching
        menu.Items.Add("偏好设置…", null, delegate { EditOptions(); });
        menu.Items.Add("设置倒计时…", null, delegate { EditCountdown(); });
        countdownMode.Click += delegate
        {
            Countdown session = options.TimerSession;
            if (session != null) SetCountdown(session.WithVisibility(!session.Visible));
        };
        menu.Items.Add(countdownMode);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem themeMenu = new ToolStripMenuItem("切换主题");
        for (int i = 0; i < 4; i++)
        {
            ClockTheme t = (ClockTheme)i;
            ToolStripMenuItem item = new ToolStripMenuItem(Options.GetThemeDisplayName(t));
            item.Click += delegate { SwitchTheme(t); };
            themeItems[i] = item;
            themeMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(themeMenu);

        autoColorItem = new ToolStripMenuItem("自动配色");
        autoColorItem.Click += delegate
        {
            options.AutoColor = !options.AutoColor;
            renderer.AutoColor = options.AutoColor;
            options.Save();
            SyncMenuChecks();
            DrawFrame();
        };
        menu.Items.Add(autoColorItem);

        ToolStripMenuItem flowMenu = new ToolStripMenuItem("数字特效");
        for (int i = 0; i < flowItems.Length; i++)
        {
            ClockFlowStyle f = (ClockFlowStyle)i;
            ToolStripMenuItem item = new ToolStripMenuItem(Options.GetFlowStyleDisplayName(f));
            item.Click += delegate { SwitchFlowStyle(f); };
            flowItems[i] = item;
            flowMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(flowMenu);

        ToolStripMenuItem bgMenu = new ToolStripMenuItem("自定义背景");
        bgMenu.DropDownItems.Add("选择背景颜色…", null, delegate { PickCustomBgColor(); });
        bgMenu.DropDownItems.Add("选择背景图片…", null, delegate { PickCustomBgImage(); });
        bgMenu.DropDownItems.Add("恢复默认背景", null, delegate { ResetCustomBg(); });
        menu.Items.Add(bgMenu);

        topMostItem = new ToolStripMenuItem("始终置顶");
        topMostItem.Click += delegate { ToggleTopMost(); };
        menu.Items.Add(topMostItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("移回主屏幕", null, delegate
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 40, area.Top + 40);
            FitScreen();
            SavePosition();
            DrawFrame();
        });
        menu.Items.Add("退出", null, delegate { Close(); });
        SyncMenuChecks();

        trayIcon = LoadAppIcon();
        Icon = trayIcon;
        tray = new NotifyIcon
        {
            Text = "水流时钟 · 双击设置",
            Icon = trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += delegate { EditOptions(); };

        timer.Interval = 30;
        timer.Tick += delegate { DrawFrame(false); };
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Shown += delegate { DrawFrame(); timer.Start(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80; // WS_EX_LAYERED | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    void SyncMenuChecks()
    {
        var session = options.TimerSession;
        countdownMode.Visible = session != null;
        countdownMode.Text = session != null && session.Visible ? "切回时钟" : "显示已有倒计时";
        for (int i = 0; i < themeItems.Length; i++)
            themeItems[i].Checked = (int)options.Theme == i;
        for (int i = 0; i < flowItems.Length; i++)
            flowItems[i].Checked = (int)options.FlowStyle == i;
        topMostItem.Checked = options.TopMost;
        autoColorItem.Checked = options.AutoColor;
    }

    void SwitchTheme(ClockTheme theme)
    {
        if (options.Theme == theme) return;
        options.Theme = theme;
        options.Save();
        RebuildRenderer();
        SyncMenuChecks();
    }

    void EditCountdown()
    {
        if (editing) return;
        editing = true;
        try
        {
            using (var dialog = new CountdownDialog(options.TimerSession))
                if (dialog.ShowDialog(this) == DialogResult.OK) SetCountdown(dialog.Result);
        }
        finally { editing = false; }
    }

    void SetCountdown(Countdown session)
    {
        try { options.SaveCountdown(session); }
        catch (Exception e)
        {
            if (!(e is System.IO.IOException || e is System.Security.SecurityException || e is UnauthorizedAccessException)) throw;
            MessageBox.Show(this, e.Message, "倒计时保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SyncMenuChecks();
        DrawFrame();
    }

    void SwitchFlowStyle(ClockFlowStyle style)
    {
        if (options.FlowStyle == style) return;
        options.FlowStyle = style;
        options.Save();
        RebuildRenderer();
        SyncMenuChecks();
    }

    void ToggleTopMost()
    {
        options.TopMost = !options.TopMost;
        TopMost = options.TopMost;
        options.Save();
        SyncMenuChecks();
    }

    void PickCustomBgColor()
    {
        using (var dlg = new ColorDialog { Color = Color.FromArgb(options.CustomBgColor), FullOpen = true })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            options.CustomBgMode = CustomBackgroundMode.SolidColor;
            options.CustomBgColor = Color.FromArgb(228, dlg.Color.R, dlg.Color.G, dlg.Color.B).ToArgb();
            options.Save();
            renderer.CustomBgMode = options.CustomBgMode;
            renderer.CustomBgColor = options.CustomBgColor;
            DrawFrame(true);
        }
    }

    void PickCustomBgImage()
    {
        using (var dlg = new OpenFileDialog
        {
            Title = "选择时钟背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            options.CustomBgMode = CustomBackgroundMode.Image;
            options.CustomBgImage = dlg.FileName;
            options.Save();
            renderer.CustomBgMode = options.CustomBgMode;
            renderer.CustomBgImage = options.CustomBgImage;
            DrawFrame(true);
        }
    }

    void ResetCustomBg()
    {
        options.CustomBgMode = CustomBackgroundMode.Default;
        options.CustomBgImage = "";
        options.Save();
        renderer.CustomBgMode = options.CustomBgMode;
        renderer.CustomBgImage = "";
        DrawFrame(true);
    }

    ClockRenderer CreateRenderer()
    {
        return new ClockRenderer(options.FontName, options.Theme, options.Scale, options.FlowStyle, options.Zoom)
        {
            AutoColor = options.AutoColor,
            CustomBgMode = options.CustomBgMode,
            CustomBgColor = options.CustomBgColor,
            CustomBgImage = options.CustomBgImage
        };
    }

    void RebuildRenderer()
    {
        Point center = new Point(Left + Width / 2, Top + Height / 2);
        var updated = CreateRenderer();
        renderer.Dispose();
        renderer = updated;
        Size = renderer.Size;
        Location = new Point(center.X - Width / 2, center.Y - Height / 2);
        FitScreen();
        SavePosition();
        DrawFrame();
    }

    void FitScreen()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(
            Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
            Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
    }

    void SavePosition()
    {
        options.X = Left;
        options.Y = Top;
        options.Save();
    }

    void RefreshDesktop()
    {
        if (!IsHandleCreated || IsDisposed || Disposing) return;
        BeginInvoke((MethodInvoker)delegate
        {
            if (!IsDisposed)
            {
                wallpaper.Refresh();
                FitScreen();
                DrawFrame();
            }
        });
    }

    void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { RefreshDesktop(); }
    void DisplayChanged(object sender, EventArgs e) { RefreshDesktop(); }

    void EditOptions()
    {
        if (editing) return;
        editing = true;
        try
        {
            using (var dialog = new OptionsDialog(options))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
            }
            TopMost = options.TopMost;
            SyncMenuChecks();
            RebuildRenderer();
        }
        finally { editing = false; }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        resizing = ResizeGrip(ClientSize).Contains(e.Location);
        dragging = !resizing;
        initialZoom = previewZoom = options.Zoom;
        dragCursor = Cursor.Position;
        dragLocation = Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = resizing || ResizeGrip(ClientSize).Contains(e.Location) ? Cursors.SizeNWSE : Cursors.Default;
        if (resizing)
        {
            Point pointer = Cursor.Position;
            previewZoom = ZoomAfterDrag(renderer.PresetSize, initialZoom, pointer.X - dragCursor.X, pointer.Y - dragCursor.Y);
            Size target = ClockRenderer.SizeAtZoom(renderer.PresetSize, previewZoom);
            if (Size != target)
            {
                Size = target;
                DrawFrame();
            }
            return;
        }
        if (!dragging) return;
        Point p = Cursor.Position;
        Location = new Point(dragLocation.X + p.X - dragCursor.X, dragLocation.Y + p.Y - dragCursor.Y);
        DrawFrame();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if ((dragging || resizing) && e.Button == MouseButtons.Left)
        {
            FinishDrag();
            Capture = false;
        }
        if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && (dragging || resizing) && !IsDisposed && !Disposing) FinishDrag();
    }

    void FinishDrag()
    {
        bool resized = resizing && options.Zoom != previewZoom;
        resizing = dragging = false;
        if (resized)
        {
            options.Zoom = previewZoom;
            var updated = CreateRenderer();
            renderer.Dispose();
            renderer = updated;
            Size = renderer.Size;
        }
        FitScreen();
        SavePosition();
        DrawFrame();
    }

    internal static int ZoomAfterDrag(Size preset, int start, int dx, int dy)
    {
        double width = preset.Width, height = preset.Height;
        double delta = (dx * width + dy * height) / (width * width + height * height);
        return Options.ClampZoom((int)Math.Round(start + delta * 10000));
    }

    internal static Rectangle ResizeGrip(Size size)
    {
        return new Rectangle(size.Width - 25, size.Height - 25, 24, 24);
    }

    internal static void PaintGrip(Bitmap frame, bool visible)
    {
        Rectangle grip = ResizeGrip(frame.Size);
        using (Graphics g = Graphics.FromImage(frame))
        using (SolidBrush hitSurface = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
        {
            // Windows sends mouse events only to nontransparent layered-window pixels.
            g.FillRectangle(hitSurface, grip);
            if (!visible) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen outline = new Pen(Color.FromArgb(175, 22, 32, 46), 3.6f))
            using (Pen light = new Pen(Color.FromArgb(235, 238, 246, 255), 1.5f))
            {
                for (int d = 5; d <= 13; d += 4)
                {
                    Point a = new Point(grip.Right - 4 - d, grip.Bottom - 4);
                    Point b = new Point(grip.Right - 4, grip.Bottom - 4 - d);
                    g.DrawLine(outline, a, b);
                    g.DrawLine(light, a, b);
                }
            }
        }
    }

    void DrawFrame(bool force = true)
    {
        if (!IsHandleCreated || IsDisposed) return;
        bool showGrip = resizing || Bounds.Contains(Cursor.Position);
        Countdown countdown = options.TimerSession;
        bool countdownVisible = countdown != null && countdown.Visible;
        DateTime nowUtc = DateTime.UtcNow;
        DateTime nowLocal = nowUtc.ToLocalTime();
        // A duration timer may expire partway through a wall-clock second.
        long sec = countdownVisible ? countdown.Remaining(nowUtc) : nowLocal.Ticks / TimeSpan.TicksPerSecond;
        if (countdownVisible && sec == 0 && options.AutoColor)
            sec = -nowUtc.Ticks / TimeSpan.TicksPerSecond;

        if (!force && !dragging && !resizing && !renderer.IsAnimating &&
            sec == lastRenderedSecond && showGrip == lastGripVisible && countdownVisible == lastCountdownVisible)
        {
            return;
        }

        lastRenderedSecond = sec;
        lastGripVisible = showGrip;
        lastCountdownVisible = countdownVisible;

        using (var bitmap = countdownVisible
            ? renderer.RenderCountdown(countdown, nowUtc, Location, wallpaper)
            : renderer.Render(nowLocal, options.Twelve, Location, wallpaper))
        {
            if (resizing && bitmap.Size != Size)
            {
                using (var preview = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(preview))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(bitmap, new Rectangle(Point.Empty, preview.Size));
                    }
                    PaintGrip(preview, true);
                    LayeredSurface.Draw(Handle, Location, preview);
                }
                return;
            }
            PaintGrip(bitmap, showGrip);
            LayeredSurface.Draw(Handle, Location, bitmap);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr handle);

    static Icon LoadAppIcon()
    {
        try
        {
            Icon embedded = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (embedded != null) return embedded;
        }
        catch { }
        return CreateTrayIcon();
    }

    static Icon CreateTrayIcon()
    {
        using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(245, 22, 28, 40)))
                    g.FillEllipse(bg, 2, 2, 28, 28);
                using (Pen ring = new Pen(Color.FromArgb(220, 130, 200, 255), 2f))
                    g.DrawEllipse(ring, 3, 3, 26, 26);
                using (Pen hand = new Pen(Color.White, 2.2f))
                {
                    hand.StartCap = LineCap.Round;
                    hand.EndCap = LineCap.Round;
                    g.DrawLine(hand, 16, 16, 16, 9);
                    g.DrawLine(hand, 16, 16, 22, 19);
                }
            }
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using (Icon temp = Icon.FromHandle(hIcon))
                    return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            SystemEvents.UserPreferenceChanged -= PreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            renderer.Dispose();
            wallpaper.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class LayeredSurface
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct Blend { public byte Operation, Flags, Alpha, Format; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr window, IntPtr desktop, ref Point dest,
        ref Size size, IntPtr source, ref Point origin, int key, ref Blend blend, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    public static void Draw(IntPtr handle, Point dest, Bitmap bitmap)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr native = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            native = bitmap.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(dc, native);
            var origin = Point.Empty;
            var size = bitmap.Size;
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(handle, IntPtr.Zero, ref dest, ref size, dc, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(dc, old);
            if (native != IntPtr.Zero) DeleteObject(native);
            DeleteDC(dc);
        }
    }
}
