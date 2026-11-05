using System.Runtime.InteropServices;

namespace Bayan.Semantics;

/// <summary>The machine the compiler runs on, as a Bayan target name, and the split of a target name into <c>Target.os</c> and <c>Target.arch</c>.</summary>
public static class HostTarget
{
    /// <summary>The machine this runs on (<c>linux-x64</c>, …), or null on one with no Bayan target.</summary>
    public static string? Detect()
    {
        var os = OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : null;
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return os is null || arch is null ? null : os + "-" + arch;
    }

    /// <summary>The <c>Target.os</c> and <c>Target.arch</c> of <paramref name="target"/>; <c>wasm32</c> is <c>wasi</c>/<c>wasm32</c>.</summary>
    public static (string Os, string Arch) Split(string target)
    {
        if (target == "wasm32") return ("wasi", "wasm32");
        var dash = target.IndexOf('-');
        return dash < 0 ? ("", "") : (target[..dash], target[(dash + 1)..]);
    }
}
