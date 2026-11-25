using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

internal sealed class Countdown
{
    internal const int MaximumSeconds = 99 * 3600 + 59 * 60 + 59;
    internal const int MaxTargetYears = 10;
    internal const int MaximumNameLength = 80;
    public readonly string Name;
    public readonly int Duration;
    public readonly DateTime Deadline;
    public readonly bool Visible;
    public readonly bool IsTarget;
    public readonly bool ShowSecondsOnly;

    private Countdown(string name, int duration, DateTime deadline, bool visible, bool isTarget, bool showSecondsOnly)
    {
        Name = name;
        Duration = duration;
        Deadline = deadline;
        Visible = visible;
        IsTarget = isTarget;
        ShowSecondsOnly = showSecondsOnly;
    }

    public static Countdown Start(string name, int duration, DateTime utcNow, bool showSecondsOnly = false)
    {
        ValidateName(name);
        if (duration < 1 || duration > MaximumSeconds)
            throw new ArgumentOutOfRangeException("duration", "时长须在 00:00:01 至 99:59:59 之间。");
        return new Countdown(name.Trim(), duration, utcNow.AddSeconds(duration), true, false, showSecondsOnly);
    }

    public static Countdown Until(string name, DateTime localTarget, TimeZoneInfo zone, DateTime utcNow, Countdown previous, bool showSecondsOnly = false)
    {
        ValidateName(name);
        DateTime wallTime = new DateTime(localTarget.Year, localTarget.Month, localTarget.Day,
            localTarget.Hour, localTarget.Minute, localTarget.Second, DateTimeKind.Unspecified);
        DateTime utcTarget;
        if (previous != null && previous.IsTarget && TimeZoneInfo.ConvertTimeFromUtc(previous.Deadline, zone) == wallTime)
            utcTarget = previous.Deadline;
        else
        {
            if (zone.IsInvalidTime(wallTime)) throw new ArgumentException("该本地时间因夏令时跳转而不存在，请另选时间。");
            if (zone.IsAmbiguousTime(wallTime)) throw new ArgumentException("该本地时间在夏令时回拨时出现两次，请另选时间。");
            utcTarget = TimeZoneInfo.ConvertTimeToUtc(wallTime, zone);
        }
        if (utcTarget <= utcNow) throw new ArgumentException("结束时间必须晚于当前时间。");
        if (utcTarget > utcNow.AddYears(MaxTargetYears))
            throw new ArgumentException("结束日期须在未来 " + MaxTargetYears.ToString(CultureInfo.InvariantCulture) + " 年以内，避免超长时间设置。");
        return new Countdown(name.Trim(), 0, utcTarget, true, true, showSecondsOnly);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
            throw new ArgumentException("请输入 1 至 80 字的倒计时名称。");
    }

    public Countdown WithVisibility(bool visible) { return new Countdown(Name, Duration, Deadline, visible, IsTarget, ShowSecondsOnly); }

    public long Remaining(DateTime utcNow)
    {
        long ticks = Math.Max(0L, Deadline.Ticks - utcNow.Ticks);
        long seconds = (ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond;
        return IsTarget ? seconds : Math.Min(MaximumSeconds, seconds);
    }

    public static string Format(long seconds)
    {
        if (seconds > MaximumSeconds) seconds %= 86400;
        return (seconds / 3600).ToString("00", CultureInfo.InvariantCulture) + ":" +
               (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
               (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    public string FormatDisplay(long seconds)
    {
        if (ShowSecondsOnly) return seconds.ToString(CultureInfo.InvariantCulture);
        return Format(seconds);
    }

    public string Caption(long seconds)
    {
        if (seconds == 0) return "时间到 · " + Name;
        if (ShowSecondsOnly) return "剩余秒数 · " + Name;
        if (seconds > MaximumSeconds) return (seconds / 86400).ToString(CultureInfo.InvariantCulture) + " 天 · " + Name;
        return Name;
    }

    public string[] Store()
    {
        return new string[]
        {
            Name,
            Duration.ToString(CultureInfo.InvariantCulture),
            Deadline.Ticks.ToString(CultureInfo.InvariantCulture),
            Visible ? "1" : "0",
            IsTarget ? "target" : "duration",
            ShowSecondsOnly ? "seconds" : "hms"
        };
    }

    public static Countdown Restore(string[] data)
    {
        if (data == null || data.Length < 4 || data.Length > 6 || string.IsNullOrWhiteSpace(data[0]) ||
            data[0].Length > MaximumNameLength || (data[3] != "0" && data[3] != "1")) return null;
        bool isTarget = data.Length >= 5 && data[4] == "target";
        if (data.Length >= 5 && !isTarget && data[4] != "duration") return null;
        bool showSecondsOnly = data.Length == 6 && data[5] == "seconds";
        if (data.Length == 6 && !showSecondsOnly && data[5] != "hms") return null;
        int duration;
        long ticks;
        if (!int.TryParse(data[1], NumberStyles.None, CultureInfo.InvariantCulture, out duration) ||
            (isTarget ? duration != 0 : duration < 1 || duration > MaximumSeconds) ||
            !long.TryParse(data[2], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
            ticks < 1 || ticks > DateTime.MaxValue.Ticks) return null;
        return new Countdown(data[0], duration, new DateTime(ticks, DateTimeKind.Utc), data[3] == "1", isTarget, showSecondsOnly);
    }
}

internal sealed class CountdownDialog : Form
{
    readonly TextBox nameBox = new TextBox();
    readonly NumericUpDown hours = new NumericUpDown(), minutes = new NumericUpDown(), seconds = new NumericUpDown();
    readonly RadioButton relativeMode = new RadioButton { Text = "按时长", AutoSize = true };
    readonly RadioButton targetMode = new RadioButton { Text = "指定结束日期时间（未来 10 年内）", AutoSize = true };
    readonly RadioButton displayHms = new RadioButton { Text = "正常时间（时:分:秒）", AutoSize = true };
    readonly RadioButton displaySec = new RadioButton { Text = "总秒数（纯秒倒计时）", AutoSize = true };
    readonly Panel durationFields = new Panel(), targetFields = new Panel();
    readonly DateTimePicker date = new DateTimePicker(), time = new DateTimePicker();
    readonly Label error = new Label();
    public Countdown Result { get; private set; }

    public CountdownDialog(Countdown previous)
    {
        Text = "设置倒计时";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(406, 308);

        AddLabel(this, "倒计时名称", 19, 16);
        nameBox.SetBounds(19, 39, 368, 25);
        nameBox.MaxLength = Countdown.MaximumNameLength;
        nameBox.Text = previous == null ? "" : previous.Name;
        Controls.Add(nameBox);

        relativeMode.Location = new Point(19, 76);
        targetMode.Location = new Point(108, 76);
        Controls.Add(relativeMode);
        Controls.Add(targetMode);

        durationFields.SetBounds(19, 105, 368, 65);
        targetFields.Bounds = durationFields.Bounds;
        Controls.Add(durationFields);
        Controls.Add(targetFields);

        int duration = previous == null || previous.IsTarget ? 300 : previous.Duration;
        AddNumber("小时", hours, 0, 99, duration / 3600);
        AddNumber("分钟", minutes, 126, 59, duration / 60 % 60);
        AddNumber("秒", seconds, 252, 59, duration % 60);

        AddLabel(targetFields, "目标年 / 月 / 日", 0, 0);
        AddLabel(targetFields, "时间（时:分:秒）", 196, 0);
        date.Format = DateTimePickerFormat.Custom;
        date.CustomFormat = "yyyy-MM-dd";
        date.MinDate = DateTime.Today;
        date.MaxDate = DateTime.Today.AddYears(Countdown.MaxTargetYears);
        time.Format = DateTimePickerFormat.Custom;
        time.CustomFormat = "HH:mm:ss";
        time.ShowUpDown = true;

        DateTime end = previous != null && previous.IsTarget ? previous.Deadline.ToLocalTime() : DateTime.Now.AddHours(1);
        date.Value = end < date.MinDate ? date.MinDate : end > date.MaxDate ? date.MaxDate : end;
        time.Value = date.Value;
        date.SetBounds(0, 23, 180, 25);
        time.SetBounds(196, 23, 172, 25);
        targetFields.Controls.Add(date);
        targetFields.Controls.Add(time);

        targetMode.CheckedChanged += delegate
        {
            targetFields.Visible = targetMode.Checked;
            durationFields.Visible = !targetMode.Checked;
            error.Text = "";
        };
        targetMode.Checked = previous != null && previous.IsTarget;
        relativeMode.Checked = !targetMode.Checked;
        targetFields.Visible = targetMode.Checked;
        durationFields.Visible = !targetMode.Checked;

        AddLabel(this, "显示形式", 19, 178);
        var displayGroup = new Panel();
        displayGroup.SetBounds(19, 200, 368, 26);
        displayHms.Location = new Point(0, 2);
        displaySec.Location = new Point(176, 2);
        displaySec.Checked = previous != null && previous.ShowSecondsOnly;
        displayHms.Checked = !displaySec.Checked;
        displayGroup.Controls.Add(displayHms);
        displayGroup.Controls.Add(displaySec);
        Controls.Add(displayGroup);

        error.SetBounds(19, 228, 368, 30);
        error.ForeColor = Color.Firebrick;
        Controls.Add(error);

        var start = new Button { Text = "开始倒计时" };
        start.SetBounds(188, 264, 104, 30);
        start.Click += delegate
        {
            try
            {
                bool secOnly = displaySec.Checked;
                if (targetMode.Checked)
                    Result = Countdown.Until(nameBox.Text, date.Value.Date.Add(time.Value.TimeOfDay),
                        TimeZoneInfo.Local, DateTime.UtcNow, previous, secOnly);
                else
                    Result = Countdown.Start(nameBox.Text, (int)(hours.Value * 3600 + minutes.Value * 60 + seconds.Value),
                        DateTime.UtcNow, secOnly);
            }
            catch (ArgumentException ex)
            {
                error.Text = ex is ArgumentOutOfRangeException ? "倒计时时长须大于零。" : ex.Message;
                if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Focus();
                else if (targetMode.Checked) date.Focus();
                else seconds.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(300, 264, 87, 30);
        Controls.Add(start);
        Controls.Add(cancel);
        AcceptButton = start;
        CancelButton = cancel;
        Shown += delegate { nameBox.Focus(); nameBox.SelectAll(); };
    }

    private static void AddLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
    }

    private void AddNumber(string label, NumericUpDown box, int x, int maximum, int value)
    {
        AddLabel(durationFields, label, x, 0);
        box.SetBounds(x, 23, 114, 25);
        box.Maximum = maximum;
        box.Value = value;
        durationFields.Controls.Add(box);
    }
}
