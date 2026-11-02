using Bayan.Cli;

Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

return CliApp.Run(args, Console.Out, Console.Error, new CliApp.Terminal(!Console.IsOutputRedirected, !Console.IsErrorRedirected, Width()));

// Throws or reports 0 without a console.
static int Width()
{
    try
    {
        return Console.WindowWidth;
    }
    catch (IOException)
    {
        return 0;
    }
}
