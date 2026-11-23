using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

public enum ClockTheme
{
    LiquidGlass = 0,
    PureFloating = 1,
    PorcelainLight = 2,
    AbyssalInk = 3
}

public enum ClockScale
{
    Compact = 0,
    Standard = 1,
    Large = 2
}

public enum ClockFlowStyle
{
    LiquidStream = 0,
    PrecisionCylinder = 1,
    CalmRipple = 2,
    MinimalGlide = 3
}

public class Options
{
    private const string SettingsPath = @"Software\WaterflowClock";

    public string FontName = HandwrittenDigits.BuiltInFontName;
    public bool Twelve;
    public bool TopMost;
    public bool AutoColor = true;
    public ClockTheme Theme = ClockTheme.LiquidGlass;
    public ClockScale Scale = ClockScale.Standard;
    public ClockFlowStyle FlowStyle = ClockFlowStyle.LiquidStream;
    public int X = int.MinValue;
    public int Y = int.MinValue;

    public static Options Load()
    {
        Options options = new Options();
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsPath))
            {
                if (key == null) return options;
                string savedFont = key.GetValue("Font") as string;
                if (!string.IsNullOrEmpty(savedFont))
                    options.FontName = savedFont;
                options.Twelve = ReadNumber(key, "Twelve", 0) == 1;
                options.TopMost = ReadNumber(key, "TopMost", 0) == 1;
                options.AutoColor = ReadNumber(key, "AutoColor", 1) != 0;
                int theme = ReadNumber(key, "Theme", (int)ClockTheme.LiquidGlass);
                if (theme >= 0 && theme <= 3) options.Theme = (ClockTheme)theme;
                int scale = ReadNumber(key, "Scale", (int)ClockScale.Standard);
                if (scale >= 0 && scale <= 2) options.Scale = (ClockScale)scale;
                int flow = ReadNumber(key, "FlowStyle", (int)ClockFlowStyle.LiquidStream);
                if (flow >= 0 && flow <= 3) options.FlowStyle = (ClockFlowStyle)flow;
                options.X = ReadNumber(key, "X", int.MinValue);
                options.Y = ReadNumber(key, "Y", int.MinValue);
            }
        }
        catch (IOException) { }
        catch (SecurityException) { }
        catch (UnauthorizedAccessException) { }
        return options;
    }

    private static int ReadNumber(RegistryKey key, string name, int fallback)
    {
        object value = key.GetValue(name);
        return value is int ? (int)value : fallback;
    }

    public void Save()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
        {
            key.SetValue("Font", FontName, RegistryValueKind.String);
            key.SetValue("Twelve", Twelve ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("TopMost", TopMost ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("AutoColor", AutoColor ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("Theme", (int)Theme, RegistryValueKind.DWord);
            key.SetValue("Scale", (int)Scale, RegistryValueKind.DWord);
            key.SetValue("FlowStyle", (int)FlowStyle, RegistryValueKind.DWord);
            key.SetValue("X", X, RegistryValueKind.DWord);
            key.SetValue("Y", Y, RegistryValueKind.DWord);
        }
    }

    public static string GetThemeDisplayName(ClockTheme theme)
    {
        switch (theme)
        {
            case ClockTheme.LiquidGlass: return "液态磨砂（深色玻璃）";
            case ClockTheme.PureFloating: return "纯净悬浮（无边框）";
            case ClockTheme.PorcelainLight: return "极简白瓷（浅色玻璃）";
            case ClockTheme.AbyssalInk: return "深海幽蓝（冰蓝点缀）";
            default: return "液态磨砂（深色玻璃）";
        }
    }

    public static string GetFlowStyleDisplayName(ClockFlowStyle style)
    {
        switch (style)
        {
            case ClockFlowStyle.LiquidStream: return "水波溪流（连续秒流 + 折射 + 连锁 + 微光）";
            case ClockFlowStyle.PrecisionCylinder: return "精密滚轮（3D 透视 + 轻弹 + 连锁 + 微光）";
            case ClockFlowStyle.CalmRipple: return "静谧水影（步进 + 液面折射虚化 + 连锁）";
            case ClockFlowStyle.MinimalGlide: return "纯净极简（平滑渐隐 + 波浪连锁）";
            default: return "水波溪流（连续秒流 + 折射 + 连锁 + 微光）";
        }
    }
}

public sealed class OptionsDialog : Form
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WaterflowClock";

    private readonly Options options;
    private readonly ComboBox themeBox = new ComboBox();
    private readonly ComboBox flowBox = new ComboBox();
    private readonly ComboBox scaleBox = new ComboBox();
    private readonly ComboBox fontsBox = new ComboBox();
    private readonly ComboBox formatBox = new ComboBox();
    private readonly CheckBox topMostCheck = new CheckBox();
    private readonly CheckBox autoColorCheck = new CheckBox();
    private readonly CheckBox startupCheck = new CheckBox();
    private readonly bool initiallyEnabled;

    private static readonly Color BgColor = Color.FromArgb(24, 26, 32);
    private static readonly Color SurfaceColor = Color.FromArgb(34, 37, 46);
    private static readonly Color TextPrimary = Color.FromArgb(240, 244, 248);
    private static readonly Color TextMuted = Color.FromArgb(156, 163, 175);
    private static readonly Color AccentColor = Color.FromArgb(99, 179, 237);

    public OptionsDialog(Options options)
    {
        this.options = options;

        Text = "水流时钟 · 偏好设置";
        Font = CreateUiFont(9.5f, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgColor;
        ForeColor = TextPrimary;
        ClientSize = new Size(462, 432);

        Label header = new Label
        {
            Text = "外观与流动动效",
            Font = CreateUiFont(13f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(26, 20)
        };
        Label subtitle = new Label
        {
            Text = "左键拖动桌面时钟调整位置，右键可快速切换主题与流动风格",
            Font = CreateUiFont(8.5f, FontStyle.Regular),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(28, 48)
        };

        int labelX = 28;
        int fieldX = 122;
        int fieldW = 312;
        int y = 84;
        int rowStep = 40;

        Label themeLabel = MakeLabel("视觉主题", labelX, y + 4);
        StyleCombo(themeBox, fieldX, y, fieldW);
        themeBox.Items.AddRange(new object[]
        {
            Options.GetThemeDisplayName(ClockTheme.LiquidGlass),
            Options.GetThemeDisplayName(ClockTheme.PureFloating),
            Options.GetThemeDisplayName(ClockTheme.PorcelainLight),
            Options.GetThemeDisplayName(ClockTheme.AbyssalInk)
        });
        themeBox.SelectedIndex = (int)options.Theme;

        y += rowStep;
        Label flowLabel = MakeLabel("流动风格", labelX, y + 4);
        StyleCombo(flowBox, fieldX, y, fieldW);
        flowBox.Items.AddRange(new object[]
        {
            Options.GetFlowStyleDisplayName(ClockFlowStyle.LiquidStream),
            Options.GetFlowStyleDisplayName(ClockFlowStyle.PrecisionCylinder),
            Options.GetFlowStyleDisplayName(ClockFlowStyle.CalmRipple),
            Options.GetFlowStyleDisplayName(ClockFlowStyle.MinimalGlide)
        });
        flowBox.SelectedIndex = (int)options.FlowStyle;

        y += rowStep;
        Label scaleLabel = MakeLabel("时钟尺寸", labelX, y + 4);
        StyleCombo(scaleBox, fieldX, y, fieldW);
        scaleBox.Items.AddRange(new object[] { "紧凑（小）", "标准（中）", "醒目（大）" });
        scaleBox.SelectedIndex = (int)options.Scale;

        y += rowStep;
        Label fontLabel = MakeLabel("数字字体", labelX, y + 4);
        StyleCombo(fontsBox, fieldX, y, fieldW);
        using (InstalledFontCollection installed = new InstalledFontCollection())
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (FontFamily family in installed.Families)
            {
                using (family)
                    names.Add(family.Name);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            fontsBox.Items.Add(HandwrittenDigits.BuiltInFontName);
            foreach (string name in names)
                fontsBox.Items.Add(name);
        }
        fontsBox.SelectedIndex = fontsBox.FindStringExact(options.FontName);
        if (fontsBox.SelectedIndex < 0) fontsBox.SelectedIndex = 0;

        y += rowStep;
        Label formatLabel = MakeLabel("时间制式", labelX, y + 4);
        StyleCombo(formatBox, fieldX, y, fieldW);
        formatBox.Items.AddRange(new object[] { "24 小时制（00:00:00）", "12 小时制（12:00:00）" });
        formatBox.SelectedIndex = options.Twelve ? 1 : 0;

        y += rowStep + 4;
        autoColorCheck.Text = "自动配色（随背景明暗调整）";
        autoColorCheck.ForeColor = TextPrimary;
        autoColorCheck.AutoSize = true;
        autoColorCheck.Location = new Point(fieldX, y);
        autoColorCheck.Checked = options.AutoColor;

        y += 28;
        topMostCheck.Text = "始终置顶（显示在其他窗口之上）";
        topMostCheck.ForeColor = TextPrimary;
        topMostCheck.AutoSize = true;
        topMostCheck.Location = new Point(fieldX, y);
        topMostCheck.Checked = options.TopMost;

        y += 28;
        startupCheck.Text = "登录 Windows 时自动启动";
        startupCheck.ForeColor = TextPrimary;
        startupCheck.AutoSize = true;
        startupCheck.Location = new Point(fieldX, y);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunPath))
            {
                initiallyEnabled = key != null && string.Equals(
                    key.GetValue(RunValue) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
            }
            startupCheck.Checked = initiallyEnabled;
        }
        catch (SecurityException) { startupCheck.Enabled = false; }
        catch (UnauthorizedAccessException) { startupCheck.Enabled = false; }
        catch (IOException) { startupCheck.Enabled = false; }

        Button save = new Button
        {
            Text = "应用保存",
            Location = new Point(256, 380),
            Size = new Size(92, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.FromArgb(15, 23, 42),
            Font = CreateUiFont(9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += SaveClick;

        Button cancel = new Button
        {
            Text = "取消",
            Location = new Point(356, 380),
            Size = new Size(78, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceColor,
            ForeColor = TextPrimary,
            DialogResult = DialogResult.Cancel,
            Cursor = Cursors.Hand
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(58, 63, 78);
        cancel.FlatAppearance.BorderSize = 1;

        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            header, subtitle,
            themeLabel, themeBox,
            flowLabel, flowBox,
            scaleLabel, scaleBox,
            fontLabel, fontsBox,
            formatLabel, formatBox,
            autoColorCheck, topMostCheck, startupCheck,
            save, cancel
        });
    }

    private static Font CreateUiFont(float size, FontStyle style)
    {
        try { return new Font("Microsoft YaHei UI", size, style); }
        catch { return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style); }
    }

    private static Label MakeLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(x, y)
        };
    }

    private static void StyleCombo(ComboBox box, int x, int y, int width)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.FlatStyle = FlatStyle.Flat;
        box.BackColor = SurfaceColor;
        box.ForeColor = TextPrimary;
        box.SetBounds(x, y, width, 28);
    }

    private static string StartupCommand
    {
        get { return "\"" + Application.ExecutablePath + "\""; }
    }

    private void SaveClick(object sender, EventArgs args)
    {
        Options edited = new Options
        {
            X = options.X,
            Y = options.Y,
            FontName = fontsBox.SelectedItem as string ?? options.FontName,
            Twelve = formatBox.SelectedIndex == 1,
            TopMost = topMostCheck.Checked,
            AutoColor = autoColorCheck.Checked,
            Theme = (ClockTheme)Math.Max(0, themeBox.SelectedIndex),
            Scale = (ClockScale)Math.Max(0, scaleBox.SelectedIndex),
            FlowStyle = (ClockFlowStyle)Math.Max(0, flowBox.SelectedIndex)
        };
        try
        {
            if (startupCheck.Enabled && startupCheck.Checked != initiallyEnabled)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath))
                {
                    if (startupCheck.Checked)
                        key.SetValue(RunValue, StartupCommand, RegistryValueKind.String);
                    else
                        key.DeleteValue(RunValue, false);
                }
            }
            edited.Save();
            options.FontName = edited.FontName;
            options.Twelve = edited.Twelve;
            options.TopMost = edited.TopMost;
            options.AutoColor = edited.AutoColor;
            options.Theme = edited.Theme;
            options.Scale = edited.Scale;
            options.FlowStyle = edited.FlowStyle;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            if (!(ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)) throw;
            MessageBox.Show(this, ex.Message, "设置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
