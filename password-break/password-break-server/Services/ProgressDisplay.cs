using Spectre.Console;
using password_break_server.Models;
using System.Collections.Concurrent;

namespace password_break_server.Services;

public class ProgressDisplay : IServerEventListener, IDisposable
{
    private readonly PasswordBreakConfig _config;
    private readonly TaskManager _taskManager;
    private readonly FoundPasswords _foundPasswords;
    private readonly ClientTracker _clientTracker;
    private readonly DisplayRenderer _renderer = new();
    private readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogLines = 1000;
    private volatile bool _running;

    public bool ShowWorkers { get; set; } = true;
    public bool ShowTasks { get; set; } = true;
    public bool ShowLog { get; set; } = true;

    public ProgressDisplay(
        PasswordBreakConfig config,
        TaskManager taskManager,
        FoundPasswords foundPasswords,
        ClientTracker clientTracker)
    {
        _config = config;
        _taskManager = taskManager;
        _foundPasswords = foundPasswords;
        _clientTracker = clientTracker;
    }

    public void Start()
    {
        _running = true;
        Console.Clear();
        Console.CursorVisible = false;
        Task.Run(async () =>
        {
            while (_running)
            {
                HandleInput();
                Render();
                await Task.Delay(500);
            }
        });
    }

    private void HandleInput()
    {
        try
        {
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.D1: ShowWorkers = !ShowWorkers; _renderer.Reset(); break;
                    case ConsoleKey.D2: ShowTasks = !ShowTasks; _renderer.Reset(); break;
                    case ConsoleKey.D3: ShowLog = !ShowLog; _renderer.Reset(); break;
                }
            }
        }
        catch { }
    }

    private void Render()
    {
        try
        {
            if (!_running) return;
            var state = BuildState(Console.WindowHeight);
            _renderer.RenderDelta(state, Console.WindowWidth);
        }
        catch { }
    }

    private DisplayState BuildState(int consoleHeight)
    {
        var (completed, total, pending) = _taskManager.GetProgress();
        var found = _foundPasswords.FoundCount;
        var remaining = _foundPasswords.RemainingCount;
        var clients = _clientTracker.GetClientStates(_config.HeartbeatTimeoutSeconds);
        var activeTasks = _taskManager.GetActiveTasks();

        int logLines;
        try
        {
            var headerLines = 4;
            var progressLines = 6;
            var workersLines = ShowWorkers ? Math.Max(2, clients.Count + 4) : 2;
            var tasksLines = ShowTasks ? Math.Max(2, activeTasks.Count + 4) : 2;
            var reserved = headerLines + progressLines + workersLines + tasksLines + 4;
            logLines = Math.Max(5, consoleHeight - reserved);
        }
        catch
        {
            logLines = 10;
        }

        var actualLogLines = _logs.Skip(Math.Max(0, _logs.Count - logLines)).ToList();

        return new DisplayState
        {
            Completed = completed, Total = total, Pending = pending,
            Found = found, Remaining = remaining,
            AllFound = remaining == 0 && found > 0,
            Saved = _foundPasswords.Saved,
            ActiveTasks = activeTasks,
            Clients = clients,
            LogLines = actualLogLines,
            ShowWorkers = ShowWorkers, ShowTasks = ShowTasks, ShowLog = ShowLog,
            AttackMode = _config.AttackMode,
            ConsoleHeight = consoleHeight
        };
    }

    public void Stop() => _running = false;

    public void ShowFinal()
    {
        _running = false;
        try
        {
            var state = BuildState(Console.WindowHeight);
            Console.Clear();
            _renderer.Render(state, Console.WindowWidth, Console.Out);
            Console.WriteLine();
            Console.CursorVisible = true;
        }
        catch { Console.CursorVisible = true; }
    }

    private void AddLog(string message)
    {
        _logs.Enqueue($"[dim]{DateTime.Now:HH:mm:ss}[/] {message}");
        while (_logs.Count > MaxLogLines) _logs.TryDequeue(out _);
    }

    // IServerEventListener — display-only, no state mutation
    public void ClientConnected(string clientId, string ip) =>
        AddLog($"[green]+[/] Client [white]{clientId}[/] ([dim]{ip.EscapeMarkup()}[/]) connected");

    public void ClientDisconnected(string clientId) =>
        AddLog($"[red]-[/] Client [white]{clientId}[/] disconnected");

    public void LogFound(string password, string hash) =>
        AddLog($"[bold green]★[/] Found: [white]{password.EscapeMarkup()}[/] → [dim]{hash[..Math.Min(16, hash.Length)]}…[/]");

    public void LogTaskAssigned(string clientId, string taskId) =>
        AddLog($"[cyan]→[/] Task [white]{taskId}[/] → [white]{clientId}[/]");

    public void LogTaskCompleted(string taskId) => AddLog($"[green]✓[/] Task [white]{taskId}[/] done");
    public void LogTaskRequeued(string taskId) => AddLog($"[yellow]↺[/] Task [white]{taskId}[/] requeued");

    public void Dispose() { Stop(); Console.CursorVisible = true; }
}
