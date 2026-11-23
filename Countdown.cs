using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

internal sealed class Countdown
{
    internal const int MaximumSeconds = 99 * 3600 + 59 * 60 + 59;
    internal const int MaximumNameLength = 80;
    public readonly string Name;
    public readonly int Duration;
    public readonly DateTime Deadline;
    public readonly bool Visible;

    private Countdown(string name, int duration, DateTime deadline, bool visible)
    {
        Name = name;
        Duration = duration;
        Deadline = deadline;
        Visible = visible;
    }

    public static Countdown Start(string name, int duration, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
            throw new ArgumentException("请输入 1 至 80 字的倒计时名称。");
        if (duration < 1 || duration > MaximumSeconds)
            throw new ArgumentOutOfRangeException("duration", "时长须在 00:00:01 至 99:59:59 之间。");
        return new Countdown(name.Trim(), duration, utcNow.AddSeconds(duration), true);
    }

    public Countdown WithVisibility(bool visible) { return new Countdown(Name, Duration, Deadline, visible); }

    public int Remaining(DateTime utcNow)
    {
        return (int)Math.Min(MaximumSeconds, Math.Max(0, Math.Ceiling((Deadline - utcNow).TotalSeconds)));
    }

    public static string Format(int seconds)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
    }

    public string[] Store()
    {
        return new string[] { Name, Duration.ToString(CultureInfo.InvariantCulture),
            Deadline.Ticks.ToString(CultureInfo.InvariantCulture), Visible ? "1" : "0" };
    }

    public static Countdown Restore(string[] data)
    {
        if (data == null || data.Length != 4 || string.IsNullOrWhiteSpace(data[0]) ||
            data[0].Length > MaximumNameLength || (data[3] != "0" && data[3] != "1")) return null;
        int duration;
        long ticks;
        if (!int.TryParse(data[1], NumberStyles.None, CultureInfo.InvariantCulture, out duration) ||
            duration < 1 || duration > MaximumSeconds ||
            !long.TryParse(data[2], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ||
            ticks < 1 || ticks > DateTime.MaxValue.Ticks) return null;
        return new Countdown(data[0], duration, new DateTime(ticks, DateTimeKind.Utc), data[3] == "1");
    }
}

internal sealed class CountdownDialog : Form
{
    readonly TextBox nameBox = new TextBox();
    readonly NumericUpDown hours = new NumericUpDown(), minutes = new NumericUpDown(), seconds = new NumericUpDown();
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
        ClientSize = new Size(382, 219);
        AddLabel("倒计时名称", 19, 16);
        nameBox.SetBounds(19, 39, 344, 25);
        nameBox.MaxLength = Countdown.MaximumNameLength;
        nameBox.Text = previous == null ? "" : previous.Name;
        Controls.Add(nameBox);
        int duration = previous == null ? 300 : previous.Duration;
        AddNumber("小时", hours, 19, 99, duration / 3600);
        AddNumber("分钟", minutes, 138, 59, duration / 60 % 60);
        AddNumber("秒", seconds, 257, 59, duration % 60);
        error.SetBounds(19, 142, 345, 25);
        error.ForeColor = Color.Firebrick;
        Controls.Add(error);
        var start = new Button { Text = previous == null ? "开始倒计时" : "重新开始" };
        start.SetBounds(164, 174, 102, 30);
        start.Click += delegate
        {
            try { Result = Countdown.Start(nameBox.Text, (int)(hours.Value * 3600 + minutes.Value * 60 + seconds.Value), DateTime.UtcNow); }
            catch (ArgumentException)
            {
                error.Text = string.IsNullOrWhiteSpace(nameBox.Text) ? "请输入倒计时名称。" : "倒计时时长须大于零。";
                if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Focus(); else seconds.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(276, 174, 87, 30);
        Controls.Add(start);
        Controls.Add(cancel);
        AcceptButton = start;
        CancelButton = cancel;
        Shown += delegate { nameBox.Focus(); nameBox.SelectAll(); };
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
    }

    private void AddNumber(string label, NumericUpDown box, int x, int maximum, int value)
    {
        AddLabel(label, x, 80);
        box.SetBounds(x, 103, 106, 25);
        box.Maximum = maximum;
        box.Value = value;
        Controls.Add(box);
    }
}
