using Bayan.Cli;

return CliApp.Run(args, Console.Out, Console.Error, new CliApp.Terminal(!Console.IsOutputRedirected, !Console.IsErrorRedirected));
