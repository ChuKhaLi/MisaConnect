using MisaConnect.EInvoice.Client;

namespace MisaConnect.EInvoice.IntegrationTests.Sandbox;

/// <summary>
/// Slice 2 T070 (research R-DEL-11) — opt-in <see cref="IAsyncDisposable"/>
/// helper that deletes a tracked sandbox draft on scope exit. Use it from
/// sandbox tests that should leave no residue behind:
///
/// <code>
/// var refId = await CreateDraft();
/// await using var cleanup = SandboxDraftCleanup.Track(client, refId, withCode: true);
/// // ... assertions ...
/// </code>
///
/// On disposal it calls <c>DeleteDraftAsync</c> and ignores any non-Deleted
/// outcome — the test may have already deleted the draft itself, which is
/// the FR-037 idempotency guarantee in action.
/// </summary>
internal sealed class SandboxDraftCleanup : IAsyncDisposable
{
    private readonly IMisaEInvoiceClient _client;
    private readonly string _refId;
    private readonly bool _invoiceWithCode;
    private bool _disposed;

    private SandboxDraftCleanup(IMisaEInvoiceClient client, string refId, bool invoiceWithCode)
    {
        _client = client;
        _refId = refId;
        _invoiceWithCode = invoiceWithCode;
    }

    public static SandboxDraftCleanup Track(IMisaEInvoiceClient client, string refId, bool invoiceWithCode)
        => new(client, refId, invoiceWithCode);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _ = await _client.DeleteDraftAsync(_refId, _invoiceWithCode);
        }
        catch
        {
            // Cleanup is best-effort; any exception is swallowed (the test
            // already passed/failed on the assertions it cared about).
        }
    }
}
