using Microsoft.Extensions.Logging;
using password_break_client;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:5210";

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}));

var wordlistManager = new WordlistManager(loggerFactory.CreateLogger<WordlistManager>());
var client = new GrpcClient(serverUrl, wordlistManager, loggerFactory.CreateLogger<GrpcClient>());
await client.RunAsync();
