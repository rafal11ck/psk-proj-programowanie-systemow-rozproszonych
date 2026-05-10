using password_break_server.Models;
using password_break_server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));

builder.Services.AddGrpc();

builder.Services.AddSingleton<ServerRunState>();

builder.Services.AddSingleton(_ =>
{
    var config = new PasswordBreakConfig();
    builder.Configuration.GetSection("PasswordBreak").Bind(config);
    return config;
});

builder.Services.AddSingleton<FoundPasswords>(sp =>
{
    var config = sp.GetRequiredService<PasswordBreakConfig>();
    return new FoundPasswords(config.TargetHashes);
});
builder.Services.AddSingleton<IFoundPasswords>(sp => sp.GetRequiredService<FoundPasswords>());

builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<ITaskManager>(sp => sp.GetRequiredService<TaskManager>());

builder.Services.AddSingleton<ClientTracker>();
builder.Services.AddSingleton<IClientTracker>(sp => sp.GetRequiredService<ClientTracker>());

builder.Services.AddSingleton<ExperimentRunManager>();

builder.Services.AddSingleton<MonitorEventBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorEventBroadcaster>());

builder.Services.AddSingleton<ConsoleLoggingEventListener>();
builder.Services.AddHostedService<TaskTimeoutChecker>();

builder.Services.AddSingleton<IServerEventListener>(sp =>
    new CompositeServerEventListener(
        new IServerEventListener[]
        {
            sp.GetRequiredService<ConsoleLoggingEventListener>(),
            sp.GetRequiredService<MonitorEventBroadcaster>(),
        },
        sp.GetRequiredService<ILogger<CompositeServerEventListener>>()));

var app = builder.Build();

app.MapGrpcService<PasswordBreakerService>();
app.MapGrpcService<MonitorGrpcService>();

app.MapGet("/", () => "Password Breaker gRPC Server");

app.MapGet("/wordlist", (PasswordBreakConfig cfg, IWebHostEnvironment env) =>
{
    if (string.IsNullOrWhiteSpace(cfg.WordListPath))
        return Results.NotFound();

    var fullPath = Path.IsPathRooted(cfg.WordListPath)
        ? cfg.WordListPath
        : Path.Combine(env.ContentRootPath, cfg.WordListPath);

    if (!File.Exists(fullPath))
        return Results.NotFound();

    return Results.File(File.OpenRead(fullPath), "text/plain");
});

var foundPasswords = app.Services.GetRequiredService<IFoundPasswords>();
var concreteFoundPasswords = app.Services.GetRequiredService<FoundPasswords>();
var runState = app.Services.GetRequiredService<ServerRunState>();
var runManager = app.Services.GetRequiredService<ExperimentRunManager>();
var clientTracker = app.Services.GetRequiredService<ClientTracker>();

runState.StateChanged += isRunning =>
{
    Console.WriteLine();

    if (isRunning)
    {
        runManager.StartNewRun(clientTracker.Count);

        concreteFoundPasswords.Reset();

        Console.WriteLine("[SERVER] START - clients can receive tasks");
    }
    else
    {
        runManager.PauseCurrentRun();

        Console.WriteLine("[SERVER] PAUSE - new tasks will not be assigned");
    }

    Console.WriteLine();
};

Console.WriteLine();
Console.WriteLine("====================================================");
Console.WriteLine(" Password Breaker Server");
Console.WriteLine(" SPACJA  - start/pause clients");
Console.WriteLine(" CTRL+C  - stop server and save results");
Console.WriteLine(" Current state: PAUSE");
Console.WriteLine("====================================================");
Console.WriteLine();

var lastSpacePress = DateTime.MinValue;

var keyboardTask = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        try
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Spacebar)
                {
                    var now = DateTime.UtcNow;

                    if ((now - lastSpacePress).TotalMilliseconds >= 500)
                    {
                        lastSpacePress = now;
                        runState.Toggle();
                    }
                }
            }

            await Task.Delay(50, app.Lifetime.ApplicationStopping);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch
        {
            await Task.Delay(500);
        }
    }
});

foundPasswords.OnAllFound += () => SaveResults(foundPasswords, runManager);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    SaveResults(foundPasswords, runManager);
    Environment.Exit(0);
};

await app.RunAsync();

SaveResults(foundPasswords, runManager);

static void SaveResults(IFoundPasswords found, ExperimentRunManager runManager)
{
    var run = runManager.Current;
    var resultPath = run?.ResultsFilePath ?? "results.csv";

    if (found.FoundCount > 0 && !found.Saved)
        found.SaveToFile(resultPath);

    if (found.FoundCount > 0)
        Console.WriteLine($"Results saved to {resultPath} ({found.FoundCount} password{(found.FoundCount > 1 ? "s" : "")} found)");

    Console.WriteLine("Server stopped.");
}
