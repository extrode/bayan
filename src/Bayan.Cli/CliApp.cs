namespace Bayan.Cli;

public static class CliApp
{
    public const string Version = "0.1.0-dev";

    public const int ExitOk = 0;
    public const int ExitUsage = 2;

    private const string Usage = """
        usage: bayan <command> [args]

        commands:
          --version    print the compiler version
          --help       print this message
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        switch (args[0])
        {
            case "--version":
            case "version":
                stdout.WriteLine($"bayan {Version}");
                return ExitOk;
            case "--help":
            case "-h":
            case "help":
                stdout.WriteLine(Usage);
                return ExitOk;
            default:
                stderr.WriteLine($"bayan: unknown command `{args[0]}`");
                stderr.WriteLine(Usage);
                return ExitUsage;
        }
    }
}
