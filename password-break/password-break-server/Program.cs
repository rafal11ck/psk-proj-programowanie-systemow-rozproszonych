using password_break_server.Models;
using password_break_server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddSingleton(_ =>
{
    var config = new PasswordBreakConfig();
    builder.Configuration.GetSection("PasswordBreak").Bind(config);
    return config;
});

builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<HashStorage>();
builder.Services.AddHostedService<ExpiredTaskChecker>();

var app = builder.Build();

app.MapGrpcService<PasswordBreakerService>();
app.MapGet("/", () => "Password Breaker gRPC Server");

app.Run();