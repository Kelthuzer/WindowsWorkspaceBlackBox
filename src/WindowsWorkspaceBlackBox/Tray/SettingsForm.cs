using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Tray;

internal sealed class SettingsForm : Form
{
    private readonly RadioButton auto = new() { Text = "Автоматически после входа в Windows", AutoSize = true };
    private readonly RadioButton manual = new() { Text = "Только по нажатию пользователя", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Запускать вместе с Windows", AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 60, Width = 100 };
    private readonly NumericUpDown retention = new() { Minimum = 1, Maximum = 1000, Width = 100 };
    private readonly DataGridView applications = new()
    {
        Width = 690, Height = 230, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    internal SettingsForm(Settings settings, IEnumerable<WindowEntry> knownWindows)
    {
        Text = "Настройки — Windows Workspace BlackBox";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(750, 650); MinimumSize = new(700, 580);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(20), AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Восстановление после входа:", AutoSize = true });
        layout.Controls.Add(auto); layout.Controls.Add(manual); layout.Controls.Add(startup);
        layout.Controls.Add(new Label { Text = "Интервал сохранения, минут:", AutoSize = true }); layout.Controls.Add(interval);
        layout.Controls.Add(new Label { Text = "Количество снимков:", AutoSize = true }); layout.Controls.Add(retention);
        layout.Controls.Add(new Label
        {
            Text = "Приложения под наблюдением. «Запускать» разрешает WWBB открыть отсутствующее приложение:",
            AutoSize = true
        });
        applications.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Track", HeaderText = "Следить", Width = 60 });
        applications.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Приложение", Width = 150, ReadOnly = true });
        applications.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "EXE", Width = 390, ReadOnly = true });
        applications.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Launch", HeaderText = "Запускать", Width = 75 });
        var known = knownWindows.Where(w => !string.IsNullOrWhiteSpace(w.ExePath) && w.ProcessName != "explorer")
            .Select(w => (w.ProcessName, w.ExePath)).DistinctBy(x => x.ExePath, StringComparer.OrdinalIgnoreCase);
        var rows = settings.MonitoredApplications.Select(r => (r.ProcessName, r.ExePath))
            .Concat(known).DistinctBy(x => string.IsNullOrWhiteSpace(x.ExePath) ? x.ProcessName : x.ExePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.ProcessName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var app in rows)
        {
            var rule = settings.MonitoredApplications.FirstOrDefault(r =>
                (!string.IsNullOrWhiteSpace(r.ExePath) && r.ExePath.Equals(app.ExePath, StringComparison.OrdinalIgnoreCase))
                || (string.IsNullOrWhiteSpace(r.ExePath) && r.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)));
            applications.Rows.Add(rule != null, app.ProcessName, app.ExePath, rule?.Mode == ApplicationRestoreMode.LaunchIfMissing);
        }
        var launchColumnIndex = applications.Columns["Launch"]!.Index;
        applications.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == launchColumnIndex
                && applications.Rows[e.RowIndex].Cells["Launch"].Value is true)
                applications.Rows[e.RowIndex].Cells["Track"].Value = true;
        };
        applications.CurrentCellDirtyStateChanged += (_, _) =>
        { if (applications.IsCurrentCellDirty) applications.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        layout.Controls.Add(applications);
        var add = new Button { Text = "Добавить EXE…", AutoSize = true };
        add.Click += (_, _) => AddExecutable();
        layout.Controls.Add(add);
        var save = new Button { Text = "Сохранить", AutoSize = true, DialogResult = DialogResult.OK };
        layout.Controls.Add(save); Controls.Add(layout); AcceptButton = save;
        auto.Checked = settings.AutoRestore; manual.Checked = !settings.AutoRestore; startup.Checked = settings.StartWithWindows;
        interval.Value = settings.IntervalMinutes; retention.Value = settings.Retention;
    }
    internal void Apply(Settings settings)
    {
        settings.AutoRestore = auto.Checked; settings.StartWithWindows = startup.Checked;
        settings.IntervalMinutes = (int)interval.Value; settings.Retention = (int)retention.Value;
        settings.ApplicationRulesConfigured = true;
        settings.MonitoredApplications = applications.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells["Track"].Value is true)
            .Select(row => new ApplicationRule
            {
                ProcessName = row.Cells["Name"].Value?.ToString() ?? "",
                ExePath = row.Cells["Path"].Value?.ToString() ?? "",
                Mode = row.Cells["Launch"].Value is true ? ApplicationRestoreMode.LaunchIfMissing : ApplicationRestoreMode.ObserveOnly
            }).ToList();
        settings.ExcludedExecutables = [];
    }

    private void AddExecutable()
    {
        using var picker = new OpenFileDialog { Title = "Добавить приложение", Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (applications.Rows.Cast<DataGridViewRow>().Any(r => picker.FileName.Equals(r.Cells["Path"].Value?.ToString(), StringComparison.OrdinalIgnoreCase))) return;
        applications.Rows.Add(true, Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName, false);
    }
}
