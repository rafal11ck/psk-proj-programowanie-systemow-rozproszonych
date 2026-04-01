using password_break_client;

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:5210";
var client = new GrpcClient(serverUrl);
await client.RunAsync();
