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
var runState = app.Services.GetRequiredService<ServerRunState>();

runState.StateChanged += isRunning =>
{
    Console.WriteLine();
    Console.WriteLine(isRunning
        ? "[SERVER] START - clients can receive tasks"
        : "[SERVER] PAUSE - new tasks will not be assigned");
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
                    runState.Toggle();
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

foundPasswords.OnAllFound += () => SaveResults(foundPasswords);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    SaveResults(foundPasswords);
    Environment.Exit(0);
};

await app.RunAsync();

SaveResults(foundPasswords);

static void SaveResults(IFoundPasswords found)
{
    if (found.FoundCount > 0 && !found.Saved)
        found.SaveToFile("results.csv");

    if (found.FoundCount > 0)
        Console.WriteLine($"Results saved to results.csv ({found.FoundCount} password{(found.FoundCount > 1 ? "s" : "")} found)");

    Console.WriteLine("Server stopped.");
}