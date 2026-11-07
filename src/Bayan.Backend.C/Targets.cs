namespace Bayan.Backend.C;

/// <summary>
/// Build targets by their Bayan names (<c>linux-x64</c>, …) and the <c>zig cc -target</c> triple
/// for each. Linux targets link musl statically so the binaries run on any distribution.
/// </summary>
public static class Targets
{
    private static readonly (string Name, string Triple)[] Table =
    [
        ("linux-x64", "x86_64-linux-musl"),
        ("linux-arm64", "aarch64-linux-musl"),
        ("macos-x64", "x86_64-macos"),
        ("macos-arm64", "aarch64-macos"),
        ("windows-x64", "x86_64-windows-gnu"),
        ("windows-arm64", "aarch64-windows-gnu"),
        ("wasm32", "wasm32-wasi"),
    ];

    public static IEnumerable<string> All => Table.Select(t => t.Name);

    public static bool IsKnown(string name) => Table.Any(t => t.Name == name);

    public static string? Triple(string name) => Table.FirstOrDefault(t => t.Name == name).Triple;

    /// <summary>The machine this runs on, or null on one with no Bayan target.</summary>
    public static string? Host { get; } = Bayan.Semantics.HostTarget.Detect();

    /// <summary><c>tally</c>, <c>tally.exe</c> or <c>tally.wasm</c>.</summary>
    public static string ExecutableName(string name, string target) =>
        target.StartsWith("windows-", StringComparison.Ordinal) ? name + ".exe"
        : target == "wasm32" ? name + ".wasm"
        : name;
}
