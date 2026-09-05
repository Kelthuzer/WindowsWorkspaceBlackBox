using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Tray;

internal sealed class SettingsForm : Form
{
    private readonly RadioButton auto = new() { Text = "Автоматически после входа в Windows", AutoSize = true };
    private readonly RadioButton manual = new() { Text = "Только по нажатию пользователя", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Запускать вместе с Windows", AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 60, Width = 100 };
    private readonly NumericUpDown retention = new() { Minimum = 1, Maximum = 1000, Width = 100 };
    private readonly TextBox excluded = new() { Multiline = true, Width = 420, Height = 80, ScrollBars = ScrollBars.Vertical };
    internal SettingsForm(Settings settings)
    {
        Text = "Настройки — Windows Workspace BlackBox";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(500, 430); MinimumSize = new(510, 460);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(20), AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Восстановление после входа:", AutoSize = true });
        layout.Controls.Add(auto); layout.Controls.Add(manual); layout.Controls.Add(startup);
        layout.Controls.Add(new Label { Text = "Интервал сохранения, минут:", AutoSize = true }); layout.Controls.Add(interval);
        layout.Controls.Add(new Label { Text = "Количество снимков:", AutoSize = true }); layout.Controls.Add(retention);
        layout.Controls.Add(new Label { Text = "Не запускать при восстановлении (имена EXE без .exe):", AutoSize = true }); layout.Controls.Add(excluded);
        var save = new Button { Text = "Сохранить", AutoSize = true, DialogResult = DialogResult.OK };
        layout.Controls.Add(save); Controls.Add(layout); AcceptButton = save;
        auto.Checked = settings.AutoRestore; manual.Checked = !settings.AutoRestore; startup.Checked = settings.StartWithWindows;
        interval.Value = settings.IntervalMinutes; retention.Value = settings.Retention;
        excluded.Lines = settings.ExcludedExecutables.ToArray();
    }
    internal void Apply(Settings settings)
    {
        settings.AutoRestore = auto.Checked; settings.StartWithWindows = startup.Checked;
        settings.IntervalMinutes = (int)interval.Value; settings.Retention = (int)retention.Value;
        settings.ExcludedExecutables = excluded.Lines.Select(x => Path.GetFileNameWithoutExtension(x.Trim())).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
