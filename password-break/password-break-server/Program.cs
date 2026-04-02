using password_break_server.Models;
using password_break_server.Services;

var builder = WebApplication.CreateBuilder(args);

// Suppress logging noise
builder.Logging.AddFilter("Microsoft", LogLevel.Error);
builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Error);
builder.Logging.AddFilter("Grpc", LogLevel.Error);
builder.Logging.AddFilter("System", LogLevel.Error);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Error);

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
builder.Services.AddSingleton<ProgressDisplay>();
builder.Services.AddSingleton<IServerEventListener>(sp => sp.GetRequiredService<ProgressDisplay>());
builder.Services.AddHostedService<ExpiredTaskChecker>();

var app = builder.Build();

var progress = app.Services.GetRequiredService<ProgressDisplay>();
var foundPasswords = app.Services.GetRequiredService<FoundPasswords>();

Console.Clear();
progress.Start();

// Graceful exit on Ctrl+C
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    progress.Stop();
    progress.ShowFinal();
    Environment.Exit(0);
};

app.MapGrpcService<PasswordBreakerService>();
app.MapGet("/", () => "Password Breaker gRPC Server");
app.MapGet("/wordlist", (PasswordBreakConfig cfg) =>
{
    if (string.IsNullOrEmpty(cfg.WordListPath) || !File.Exists(cfg.WordListPath))
        return Results.NotFound();
    return Results.File(cfg.WordListPath, "text/plain");
});

app.Run();