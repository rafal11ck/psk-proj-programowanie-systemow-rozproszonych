using password_break_server.Models;
using password_break_server.Services;

var builder = WebApplication.CreateBuilder(args);

// Remove all logging providers — console belongs exclusively to the TUI
builder.Logging.ClearProviders();

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

builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<ClientTracker>();
builder.Services.AddSingleton<ProgressDisplay>();
builder.Services.AddSingleton<IServerEventListener>(sp => sp.GetRequiredService<ProgressDisplay>());
builder.Services.AddHostedService<ExpiredTaskChecker>();

var app = builder.Build();

app.MapGrpcService<PasswordBreakerService>();
app.MapGet("/", () => "Password Breaker gRPC Server");
app.MapGet("/wordlist", (PasswordBreakConfig cfg) =>
{
    if (string.IsNullOrEmpty(cfg.WordListPath) || !File.Exists(cfg.WordListPath))
        return Results.NotFound();
    return Results.File(cfg.WordListPath, "text/plain");
});

var progress = app.Services.GetRequiredService<ProgressDisplay>();
var foundPasswords = app.Services.GetRequiredService<FoundPasswords>();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    progress.Stop();
    progress.ShowFinal();
    SaveResults(foundPasswords);
    Environment.Exit(0);
};

progress.Start();
app.Run();

static void SaveResults(FoundPasswords found)
{
    if (found.FoundCount > 0 && !found.Saved)
        found.SaveToFile("results.csv");
    if (found.FoundCount > 0)
        Console.WriteLine($"Results saved to results.csv ({found.FoundCount} password{(found.FoundCount > 1 ? "s" : "")} found)");
    Console.WriteLine("Server stopped.");
}
