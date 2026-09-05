namespace WWBB.Core;

public static class Matching
{
    public static int Score(WindowEntry saved, WindowEntry live)
    {
        if (string.IsNullOrEmpty(saved.ExePath) || !saved.ExePath.Equals(live.ExePath, StringComparison.OrdinalIgnoreCase)) return -1;
        if (!saved.ClassName.Equals(live.ClassName, StringComparison.Ordinal)) return -1;
        int score = 10;
        if (saved.Title == live.Title) score += 100;
        if (!string.IsNullOrEmpty(saved.CommandLine) && saved.CommandLine == live.CommandLine) score += 40;
        if (saved.ProcessName.Equals(live.ProcessName, StringComparison.OrdinalIgnoreCase)) score += 5;
        return score;
    }

    public static WindowEntry? Best(WindowEntry saved, IEnumerable<WindowEntry> live, ISet<long> used)
        => live.Where(w => !used.Contains(w.Hwnd)).Select(w => (Window: w, Score: Score(saved, w)))
            .Where(x => x.Score >= 0).OrderByDescending(x => x.Score).Select(x => x.Window).FirstOrDefault();

    public static bool SamePath(string a, string b) => a.TrimEnd('\\', '/').Equals(b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public static Box Place(WindowEntry saved, MonitorLayout target)
    {
        var original = saved.Monitor.WorkArea;
        var area = target.WorkArea;
        double scale = target.Dpi / (double)Math.Max(1u, saved.Monitor.Dpi);
        var width = (int)Math.Clamp(saved.NormalBounds.Width * scale, Math.Min(160, area.Width), area.Width);
        var height = (int)Math.Clamp(saved.NormalBounds.Height * scale, Math.Min(100, area.Height), area.Height);
        var x = area.X + (saved.NormalBounds.X - original.X) * scale;
        var y = area.Y + (saved.NormalBounds.Y - original.Y) * scale;
        return new((int)Math.Clamp(x, area.X, area.X + area.Width - width),
            (int)Math.Clamp(y, area.Y, area.Y + area.Height - height), width, height);
    }
}
