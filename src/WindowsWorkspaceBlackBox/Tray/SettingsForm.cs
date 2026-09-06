using WWBB.Core;

namespace WindowsWorkspaceBlackBox.Tray;

internal sealed class SettingsForm : Form
{
    private readonly RadioButton auto = new() { Text = "Автоматически после входа в Windows", AutoSize = true };
    private readonly RadioButton manual = new() { Text = "Только по нажатию пользователя", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Запускать вместе с Windows", AutoSize = true };
    private readonly NumericUpDown interval = new() { Minimum = 1, Maximum = 60, Width = 100 };
    private readonly NumericUpDown retention = new() { Minimum = 1, Maximum = 1000, Width = 100 };
    private readonly ComboBox windowMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    private readonly DataGridView applications = new()
    {
        Width = 690, Height = 230, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    internal SettingsForm(Settings settings, IEnumerable<WindowEntry> knownWindows)
    {
        Text = "Настройки — Windows Workspace BlackBox";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(750, 710); MinimumSize = new(700, 580);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(20), AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Восстановление после входа:", AutoSize = true });
        layout.Controls.Add(auto); layout.Controls.Add(manual); layout.Controls.Add(startup);
        layout.Controls.Add(new Label { Text = "Интервал сохранения, минут:", AutoSize = true }); layout.Controls.Add(interval);
        layout.Controls.Add(new Label { Text = "Количество снимков:", AutoSize = true }); layout.Controls.Add(retention);
        windowMode.Items.AddRange(["Как в снимке", "Свернуть", "Показать"]);
        windowMode.SelectedIndex = Enum.IsDefined(settings.WindowMode) ? (int)settings.WindowMode : 0;
        layout.Controls.Add(new Label { Text = "Состояние окон после восстановления:", AutoSize = true });
        layout.Controls.Add(windowMode);
        layout.Controls.Add(new Label
        {
            Text = "Включено — открыть и расставить. Выключено — не трогать.",
            AutoSize = true
        });
        applications.Columns.Add(new DataGridViewTextBoxColumn { Name = "Track", HeaderText = "Восстанавливать", Width = 140, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
        applications.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Приложение", Width = 150, ReadOnly = true });
        applications.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "EXE", Width = 310, ReadOnly = true });

        var known = knownWindows.Where(w => !string.IsNullOrWhiteSpace(w.ExePath) && !w.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            .Select(w => (w.ProcessName, w.ExePath)).DistinctBy(x => x.ExePath, StringComparer.OrdinalIgnoreCase);
        var rows = settings.MonitoredApplications.Where(r => !r.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)).Select(r => (r.ProcessName, r.ExePath))
            .Concat(known).DistinctBy(x => string.IsNullOrWhiteSpace(x.ExePath) ? x.ProcessName : x.ExePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.ProcessName, StringComparer.CurrentCultureIgnoreCase);
        foreach (DataGridViewColumn column in applications.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        applications.RowTemplate.Height = 36;
        applications.Rows.Add(settings.RestoreExplorer, "Проводник Windows", "Папки и окна Проводника");
        foreach (var app in rows)
        {
            var rule = ApplicationPolicy.RuleFor(settings, new WindowEntry { ProcessName = app.ProcessName, ExePath = app.ExePath });
            applications.Rows.Add(rule != null, app.ProcessName, app.ExePath);
        }
        applications.CellPainting += PaintToggle;
        applications.CellMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.ColumnIndex == 0)
                Toggle(applications.Rows[e.RowIndex].Cells[0]);
        };
        applications.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && applications.CurrentCell?.ColumnIndex == 0)
            {
                Toggle(applications.CurrentCell);
                e.Handled = true; e.SuppressKeyPress = true;
            }
        };
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
        settings.WindowMode = (RestoreWindowMode)windowMode.SelectedIndex;
        settings.ApplicationRulesConfigured = true;
        settings.BinaryRulesConfigured = true;
        settings.RestoreExplorer = applications.Rows[0].Cells["Track"].Value is true;
        settings.MonitoredApplications = applications.Rows.Cast<DataGridViewRow>()
            .Skip(1).Where(row => row.Cells["Track"].Value is true)
            .Select(row => new ApplicationRule
            {
                ProcessName = row.Cells["Name"].Value?.ToString() ?? "",
                ExePath = row.Cells["Path"].Value?.ToString() ?? "",
                Mode = ApplicationRestoreMode.LaunchIfMissing
            }).ToList();
        settings.ExcludedExecutables = [];
    }

    private void Toggle(DataGridViewCell cell)
    {
        cell.Value = !(cell.Value is true);
        applications.InvalidateCell(cell);
    }

    private void PaintToggle(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0 || e.Graphics == null || e.CellStyle == null) return;
        e.PaintBackground(e.CellBounds, true);
        e.Paint(e.ClipBounds, DataGridViewPaintParts.Border);
        bool enabled = e.Value is true;
        float scale = DeviceDpi / 96f;
        int width = (int)(42 * scale), height = (int)(22 * scale);
        int x = e.CellBounds.X + (int)(10 * scale), y = e.CellBounds.Y + (e.CellBounds.Height - height) / 2;
        using var brush = new SolidBrush(enabled ? Color.FromArgb(0, 120, 215) : Color.Gray);
        var graphics = e.Graphics;
        var smoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(brush, x, y, height, height);
        graphics.FillEllipse(brush, x + width - height, y, height, height);
        graphics.FillRectangle(brush, x + height / 2, y, width - height, height);
        int inset = Math.Max(2, (int)(3 * scale));
        graphics.FillEllipse(Brushes.White, enabled ? x + width - height + inset : x + inset,
            y + inset, height - inset * 2, height - inset * 2);
        graphics.SmoothingMode = smoothing;
        var label = new Rectangle(x + width + inset * 2, e.CellBounds.Y,
            e.CellBounds.Right - x - width - inset * 2, e.CellBounds.Height);
        TextRenderer.DrawText(graphics, enabled ? "Вкл." : "Выкл.", applications.Font, label,
            (e.State & DataGridViewElementStates.Selected) != 0 ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        e.Handled = true;
    }

    private void AddExecutable()
    {
        using var picker = new OpenFileDialog { Title = "Добавить приложение", Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (applications.Rows.Cast<DataGridViewRow>().Any(r => picker.FileName.Equals(r.Cells["Path"].Value?.ToString(), StringComparison.OrdinalIgnoreCase))) return;
        applications.Rows.Add(false, Path.GetFileNameWithoutExtension(picker.FileName), picker.FileName);
    }
}
