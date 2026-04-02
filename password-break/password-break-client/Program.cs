using Microsoft.Extensions.Logging;
using password_break_client;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:5210";
var client = new GrpcClient(serverUrl);
await client.RunAsync();
