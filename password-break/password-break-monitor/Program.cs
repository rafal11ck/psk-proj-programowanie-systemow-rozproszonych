using Terminal.Gui;
using password_break_monitor;

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:5210";

var state = new MonitorState();
using var cts = new CancellationTokenSource();

Application.Init();

var normal = new Terminal.Gui.Attribute(ColorName16.White, ColorName16.Black);
var focus = new Terminal.Gui.Attribute(ColorName16.BrightYellow, ColorName16.DarkGray);
var hotNormal = new Terminal.Gui.Attribute(ColorName16.BrightCyan, ColorName16.Black);
var hotFocus = new Terminal.Gui.Attribute(ColorName16.BrightCyan, ColorName16.DarkGray);
var disabled = new Terminal.Gui.Attribute(ColorName16.Gray, ColorName16.Black);
var darkScheme = new ColorScheme
{
    Normal = normal,
    Focus = focus,
    HotNormal = hotNormal,
    HotFocus = hotFocus,
    Disabled = disabled
};
Colors.ColorSchemes["TopLevel"] = darkScheme;
Colors.ColorSchemes["Base"] = darkScheme;
Colors.ColorSchemes["Dialog"] = darkScheme;
Colors.ColorSchemes["Menu"] = darkScheme;
Colors.ColorSchemes["Error"] = darkScheme;

var dashboard = new DashboardView(state, serverUrl);
dashboard.ColorScheme = darkScheme;

var grpcClient = new GrpcMonitorClient(serverUrl, state, () =>
{
    Application.Invoke(() => dashboard.RefreshData());
});

var grpcTask = Task.Run(() => grpcClient.RunAsync(cts.Token));

// Tick co 1s żeby liczniki "ile temu" się odświeżały lokalnie
Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
{
    dashboard.RefreshData();
    return true;
});

// Handle Ctrl+C in Terminal.Gui (raw mode intercepts SIGINT)
dashboard.KeyDown += (_, e) =>
{
    if (e.KeyCode == (KeyCode.C | KeyCode.CtrlMask))
    {
        cts.Cancel();
        Application.RequestStop();
        e.Handled = true;
    }
};

// Handle SIGINT when Terminal.Gui is not active (startup/shutdown)
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Application.Invoke(() => Application.RequestStop());
};

Application.Run(dashboard);
dashboard.Dispose();
Application.Shutdown();

cts.Cancel();
grpcClient.Dispose();
try { await grpcTask; } catch { }
