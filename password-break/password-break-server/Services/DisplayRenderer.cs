using Spectre.Console;
using Spectre.Console.Rendering;
using password_break_server.Models;

namespace password_break_server.Services;

public class DisplayState
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Found { get; set; }
    public int Remaining { get; set; }
    public bool AllFound { get; set; }
    public bool Saved { get; set; }
    public IReadOnlyList<TaskInfo> ActiveTasks { get; set; } = [];
    public IReadOnlyList<(string Id, string Ip, int Ago, int Timeout)> Clients { get; set; } = [];
    public IReadOnlyList<string> LogLines { get; set; } = [];
    public bool ShowWorkers { get; set; } = true;
    public bool ShowTasks { get; set; } = true;
    public bool ShowLog { get; set; } = true;
    public string AttackMode { get; set; } = "";
    public int ConsoleHeight { get; set; } = 25;
}

public class DisplayRenderer
{
    private string[] _prevLines = [];
    private int _prevWidth;

    public void Reset()
    {
        _prevLines = [];
        Console.Clear();
    }

    public void Render(DisplayState state, int width, TextWriter output)
    {
        var content = BuildDisplay(state, width);
        var console = CreateConsole(output, width);
        console.Write(content);
    }

    public void RenderDelta(DisplayState state, int width)
    {
        var content = BuildDisplay(state, width);
        var rendered = RenderToString(content, width);
        var newLines = rendered.Split('\n');

        WriteChangedLines(newLines, width);

        if (state.AllFound && state.Saved)
        {
            Console.SetCursorPosition(0, newLines.Length);
            AnsiConsole.MarkupLine(
                $"\n[bold green]Results saved to results.csv ({state.Found} password{(state.Found > 1 ? "s" : "")} found)[/]");
            Console.CursorVisible = true;
        }

        _prevLines = newLines;
    }

    private static IAnsiConsole CreateConsole(TextWriter output, int width)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Out = new AnsiConsoleOutput(output),
        });
        console.Profile.Width = width;
        return console;
    }

    private string RenderToString(IRenderable renderable, int width)
    {
        var writer = new StringWriter();
        var console = CreateConsole(writer, width);
        console.Write(renderable);
        return writer.ToString();
    }

    private void WriteChangedLines(string[] newLines, int width)
    {
        var widthChanged = width != _prevWidth;
        if (widthChanged)
        {
            Console.Clear();
            _prevLines = [];
        }
        _prevWidth = width;

        var blank = new string(' ', width);

        for (var i = 0; i < newLines.Length; i++)
        {
            var newLine = newLines[i];
            var prevLine = i < _prevLines.Length ? _prevLines[i] : null;

            if (!widthChanged && newLine == prevLine)
                continue;

            Console.SetCursorPosition(0, i);
            Console.Write(newLine);

            var remaining = width - GetVisibleLength(newLine);
            if (remaining > 0)
                Console.Write(new string(' ', remaining));
        }

        for (var i = newLines.Length; i < _prevLines.Length; i++)
        {
            Console.SetCursorPosition(0, i);
            Console.Write(blank);
        }
    }

    private static int GetVisibleLength(string s)
    {
        var len = 0;
        var inEscape = false;
        foreach (var c in s)
        {
            switch (c)
            {
                case '\x1b':
                    inEscape = true;
                    continue;
                default:
                {
                    if (inEscape)
                    {
                        if (char.IsLetter(c)) inEscape = false;
                        continue;
                    }
                    len++;
                    break;
                }
            }
        }
        return len;
    }

    private IRenderable BuildDisplay(DisplayState s, int width)
    {
        var total = s.Total > 0 ? s.Total : 1;
        var targetTotal = s.Found + s.Remaining > 0 ? s.Found + s.Remaining : 1;
        var taskPercent = (double)s.Completed / total;
        var foundPercent = (double)s.Found / targetTotal;
        var inProgress = total - s.Completed - s.Pending;

        var barWidth = CalculateBarWidth(width, s.Completed, total, s.Found, targetTotal);

        var header = CreateHeader(s.AttackMode, targetTotal);
        var progressContent = CreateProgressContent(s, taskPercent, foundPercent, barWidth, total, inProgress);
        var progressPanel = WrapPanel(progressContent, "Progress", s.AllFound ? "green" : "yellow");

        var panels = new List<IRenderable> { header, progressPanel };
        panels.Add(BuildWorkersPanel(s));
        panels.Add(BuildTasksPanel(s));
        panels.Add(BuildLogPanel(s));

        return new Rows(panels);
    }

    private static IRenderable CreateHeader(string attackMode, int targetTotal) =>
        new Panel(
            $"[bold blue]Password Breaker[/] [dim]|[/] [yellow]{attackMode}[/] [dim]|[/] Targets: [white]{targetTotal}[/] [dim]|[/] [white]1[/][dim]-Workers[/] [white]2[/][dim]-Tasks[/] [white]3[/][dim]-Log[/]")
            .BorderStyle(Style.Parse("blue"))
            .RoundedBorder()
            .Expand();

    private static IRenderable CreateProgressContent(
        DisplayState s, double taskPercent, double foundPercent, int barWidth, int total, int inProgress)
    {
        var foundLine = s.AllFound
            ? $"[green]Found:[/] {CreateBar(foundPercent, barWidth)} {s.Found}/{s.Found + s.Remaining} [bold green]✓ DONE[/]{(s.Saved ? " [dim][saved][/]" : "")}"
            : $"[green]Found:[/] {CreateBar(foundPercent, barWidth)} {s.Found}/{s.Found + s.Remaining} [red]{s.Remaining} left[/]";

        return new Rows(
            new Markup($"[cyan]Tasks:[/] {CreateBar(taskPercent, barWidth)} {s.Completed}/{total} [dim]({inProgress} active, {s.Pending} pending)[/]"),
            new Markup(foundLine)
        );
    }

    private static int CalculateBarWidth(int width, int completed, int total, int found, int targetTotal)
    {
        total = total > 0 ? total : 1;
        targetTotal = targetTotal > 0 ? targetTotal : 1;

        var taskStats = $" {completed}/{total} ({total - completed} active, {0} pending)";
        var foundStats = $" {found}/{targetTotal} ✓ DONE [saved]";
        var maxStatsLen = Math.Max(taskStats.Length, foundStats.Length);
        return Math.Max(10, width - 4 - 2 - 7 - maxStatsLen);
    }

    private IRenderable BuildWorkersPanel(DisplayState s) =>
        s.ShowWorkers
            ? BuildWorkersTable(s)
            : BuildCollapsedPanel($"{s.Clients.Count} client(s) — press [white]1[/] to expand", "Workers");

    private IRenderable BuildWorkersTable(DisplayState s)
    {
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn("Status")
            .AddColumn("Client")
            .AddColumn("IP")
            .AddColumn("Last seen")
            .AddColumn("Timeout");

        if (s.Clients.Count == 0)
        {
            table.AddRow("[dim]○[/]", "[dim]waiting for connections...[/]", "", "", "");
            return WrapPanel(table, "Workers", "magenta");
        }

        foreach (var (id, ip, ago, timeout) in s.Clients)
        {
            var status = ago < 30 ? "[green]●[/]" : ago < 60 ? "[yellow]○[/]" : "[red]◌[/]";
            table.AddRow(status, id, Markup.Escape(ip), $"{ago}s", $"{timeout}s");
        }

        return WrapPanel(table, $"Workers ({s.Clients.Count})", "magenta");
    }

    private IRenderable BuildLogPanel(DisplayState s) =>
        s.ShowLog
            ? BuildLogContent(s)
            : BuildCollapsedPanel($"{s.LogLines.Count} entries — press [white]3[/] to expand", "Log");

    private IRenderable BuildLogContent(DisplayState s)
    {
        var logContent = s.LogLines.Count > 0
            ? string.Join("\n", s.LogLines)
            : "[dim]No events yet...[/]";

        return WrapPanel(new Markup(logContent), $"Log ({s.LogLines.Count})", "grey");
    }

    private IRenderable BuildTasksPanel(DisplayState s) =>
        s.ShowTasks
            ? BuildTasksTable(s)
            : BuildCollapsedPanel($"{s.ActiveTasks.Count} task(s) — press [white]2[/] to expand", "Tasks");

    private IRenderable BuildTasksTable(DisplayState s)
    {
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn("Task ID")
            .AddColumn("Client")
            .AddColumn("Range")
            .AddColumn("Running");

        if (s.ActiveTasks.Count == 0)
        {
            table.AddRow("[dim]—[/]", "[dim]no active tasks[/]", "", "");
            return WrapPanel(table, $"Tasks ({s.ActiveTasks.Count})", "cyan");
        }

        foreach (var t in s.ActiveTasks)
        {
            var elapsed = (int)(DateTime.UtcNow - t.StartedAt).TotalSeconds;
            table.AddRow(t.TaskId, t.ClientId ?? "[dim]?[/]", $"{t.StartIndex}–{t.EndIndex}", $"{elapsed}s");
        }

        return WrapPanel(table, $"Tasks ({s.ActiveTasks.Count})", "cyan");
    }

    private static IRenderable BuildCollapsedPanel(string content, string header) =>
        new Panel(new Markup($"[dim]{content}[/]"))
            .BorderStyle(Style.Parse("dim"))
            .RoundedBorder()
            .Header($"[bold]{header}[/]")
            .Expand();

    private static IRenderable WrapPanel(IRenderable content, string header, string color) =>
        new Panel(content)
            .BorderStyle(Style.Parse(color))
            .RoundedBorder()
            .Header($"[bold]{header}[/]")
            .Expand();

    private static string CreateBar(double percent, int width)
    {
        var filled = (int)(percent * width);
        filled = Math.Clamp(filled, 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
