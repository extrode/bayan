using System.Diagnostics;
using System.Text;

namespace Bayan.Tests;

/// <summary>Starts a child with redirected output decoded as UTF-8, whatever the OS code page (CP437 on Windows reads <c>é</c> as <c>├⌐</c>).</summary>
public static class Utf8Child
{
    public static Process Start(ProcessStartInfo psi)
    {
        if (psi.RedirectStandardOutput) psi.StandardOutputEncoding = new UTF8Encoding(false);
        if (psi.RedirectStandardError) psi.StandardErrorEncoding = new UTF8Encoding(false);
        return Process.Start(psi)!;
    }
}
