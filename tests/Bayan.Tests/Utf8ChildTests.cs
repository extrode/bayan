using System.Diagnostics;

namespace Bayan.Tests;

public sealed class Utf8ChildTests
{
    [Fact]
    public void ChildOutputIsDecodedAsUtf8()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, [0xC3, 0xA9]);
            var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe") : new ProcessStartInfo("cat");
            if (OperatingSystem.IsWindows()) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("type"); }
            psi.ArgumentList.Add(file);
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            using var p = Utf8Child.Start(psi);
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Equal("é", text);
        }
        finally { File.Delete(file); }
    }
}
