using Spectre.Console;
using password_break_server.Models;
using System.Collections.Concurrent;

namespace password_break_server.Services;

public class ProgressDisplay : IServerEventListener, IDisposable
{
    private readonly PasswordBreakConfig _config;
    private readonly TaskManager _taskManager;
    private readonly FoundPasswords _foundPasswords;
    private readonly DisplayRenderer _renderer = new();
    private readonly ConcurrentDictionary<string, (DateTime LastSeen, string Ip)> _clients = new();
    private readonly ConcurrentQueue<string> _logs = new();
    private const int FixedPanelLines = 8;
    private bool _running = true;
    private bool _finished;

    public bool ShowWorkers { get; set; } = true;
    public bool ShowTasks { get; set; } = true;
    public bool ShowLog { get; set; } = true;

    public ProgressDisplay(PasswordBreakConfig config, TaskManager taskManager, FoundPasswords foundPasswords)
    {
        _config = config;
        _taskManager = taskManager;
        _foundPasswords = foundPasswords;
    }

    public void Start()
    {
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
            if (_finished) return;
            var state = BuildState(Console.WindowHeight);
            _renderer.RenderDelta(state, Console.WindowWidth);
            if (state.AllFound && state.Saved) { _finished = true; _running = false; }
        }
        catch { }
    }

    private DisplayState BuildState(int consoleHeight)
    {
        var (completed, total, pending) = _taskManager.GetProgress();
        var found = _foundPasswords.FoundCount;
        var remaining = _foundPasswords.RemainingCount;
        var clients = GetClientStates();
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

    private IReadOnlyList<(string Id, string Ip, int Ago, int Timeout)> GetClientStates() =>
        _clients.OrderBy(c => c.Key).Select(c =>
        {
            var ago = (int)(DateTime.UtcNow - c.Value.LastSeen).TotalSeconds;
            return (c.Key, c.Value.Ip, ago, Math.Max(0, _config.HeartbeatTimeoutSeconds - ago));
        }).ToList();

    public void Stop() => _running = false;

    public void ShowFinal()
    {
        try
        {
            _finished = true;
            _running = false;
            var state = BuildState(Console.WindowHeight);
            Console.Clear();
            _renderer.Render(state, Console.WindowWidth, Console.Out);
            Console.WriteLine();
            if (state.Found > 0)
            {
                if (!state.Saved) _foundPasswords.SaveToFile("results.csv");
                Console.WriteLine($"Results saved to results.csv ({state.Found} password{(state.Found > 1 ? "s" : "")} found)");
            }
            Console.WriteLine("Server stopped.");
            Console.CursorVisible = true;
        }
        catch { Console.CursorVisible = true; }
    }

    private void AddLog(string message)
    {
        _logs.Enqueue($"[dim]{DateTime.Now:HH:mm:ss}[/] {message}");
        while (_logs.Count > 1000) _logs.TryDequeue(out _);
    }

    public void ClientConnected(string clientId, string ip)
    {
        _clients[clientId] = (DateTime.UtcNow, ip);
        AddLog($"[green]+[/] Client [white]{clientId}[/] ([dim]{ip.EscapeMarkup()}[/]) connected");
    }

    public void ClientDisconnected(string clientId)
    {
        _clients.TryRemove(clientId, out _);
        AddLog($"[red]-[/] Client [white]{clientId}[/] disconnected");
    }

    public void ClientHeartbeat(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var info))
            _clients[clientId] = (DateTime.UtcNow, info.Ip);
    }

    public List<string> CleanupStaleClients(int timeoutSeconds)
    {
        var staleClients = new List<string>();

        foreach (var (clientId, info) in _clients)
        {
            if ((DateTime.UtcNow - info.LastSeen).TotalSeconds > timeoutSeconds)
            {
                _clients.TryRemove(clientId, out _);
                AddLog($"[red]✕[/] Client [white]{clientId}[/] timed out");
                staleClients.Add(clientId);
            }
        }

        return staleClients;
    }

    public void LogFound(string password, string hash) =>
        AddLog($"[bold green]★[/] Found: [white]{password.EscapeMarkup()}[/] → [dim]{hash[..Math.Min(16, hash.Length)]}…[/]");

    public void LogTaskAssigned(string clientId, string taskId) =>
        AddLog($"[cyan]→[/] Task [white]{taskId}[/] → [white]{clientId}[/]");

    public void LogTaskCompleted(string taskId) => AddLog($"[green]✓[/] Task [white]{taskId}[/] done");
    public void LogTaskRequeued(string taskId) => AddLog($"[yellow]↺[/] Task [white]{taskId}[/] requeued");

    public void Dispose() { Stop(); Console.CursorVisible = true; }
}