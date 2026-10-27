using Bayan.LanguageServer;
using Bayan.LanguageServer.Rpc;

// stdio LSP host. Logs go to stderr (the client shows them in its output channel).
var transport = new JsonRpcTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
var server = new Server(transport);
return server.Run();
