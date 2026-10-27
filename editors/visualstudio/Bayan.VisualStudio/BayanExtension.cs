// Skeleton written against the VisualStudio.Extensibility LanguageServerProvider sample.
// Not built in this environment (needs Windows + VS SDK); see README.md.
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;

namespace Bayan.VisualStudio;

[VisualStudioContribution]
internal sealed class BayanExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "Bayan.VisualStudio.3f6c2a0e",
            version: ExtensionAssemblyVersion,
            publisherName: "Bayan",
            displayName: "Bayan",
            description: "Bayan language support: completion, hover, go to definition, diagnostics."),
    };
}

[VisualStudioContribution]
internal sealed class BayanLanguageServerProvider : LanguageServerProvider
{
    [VisualStudioContribution]
    public static DocumentTypeConfiguration BayanDocumentType => new("bayan")
    {
        FileExtensions = [".byn"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "Bayan Language Server",
        [DocumentFilter.FromDocumentType(BayanDocumentType)]);

    Process? _server;

    public override Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        StopServer(); // a reconnect must not leave the previous process running
        var exe = Environment.GetEnvironmentVariable("BAYAN_LSP") ?? "bayan-lsp";
        try
        {
            _server = Process.Start(new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Task.FromResult<IDuplexPipe?>(null); // not on PATH: colouring still works, no LSP
        }
        if (_server is null) return Task.FromResult<IDuplexPipe?>(null);
        IDuplexPipe pipe = new DuplexPipe(
            PipeReader.Create(_server.StandardOutput.BaseStream),
            PipeWriter.Create(_server.StandardInput.BaseStream));
        return Task.FromResult<IDuplexPipe?>(pipe);
    }

    void StopServer()
    {
        var p = _server;
        _server = null;
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        p.Dispose();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) StopServer();
        base.Dispose(isDisposing);
    }
}
