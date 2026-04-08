using password_break_server.Models;
using password_break_server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));

builder.Services.AddGrpc();

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

builder.Services.AddSingleton<ITaskManager, TaskManager>();
builder.Services.AddSingleton<IClientTracker, ClientTracker>();
builder.Services.AddSingleton<MonitorEventBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorEventBroadcaster>());
builder.Services.AddSingleton<ConsoleLoggingEventListener>();
builder.Services.AddSingleton<IServerEventListener>(sp =>
    new CompositeServerEventListener(
        new IServerEventListener[]
        {
            sp.GetRequiredService<ConsoleLoggingEventListener>(),
            sp.GetRequiredService<MonitorEventBroadcaster>(),
        },
        sp.GetRequiredService<ILogger<CompositeServerEventListener>>()));
builder.Services.AddHostedService<ExpiredTaskChecker>();

var app = builder.Build();

app.MapGrpcService<PasswordBreakerService>();
app.MapGrpcService<MonitorGrpcService>();
app.MapGet("/", () => "Password Breaker gRPC Server");
app.MapGet("/wordlist", (PasswordBreakConfig cfg) =>
{
    if (string.IsNullOrEmpty(cfg.WordListPath) || !File.Exists(cfg.WordListPath))
        return Results.NotFound();
    return Results.File(cfg.WordListPath, "text/plain");
});

var foundPasswords = app.Services.GetRequiredService<IFoundPasswords>();

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
