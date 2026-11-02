using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class ListActiveTemplates
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;

    public ListActiveTemplates(IMeInvoiceClient client, EnsureAccessToken ensureToken)
    {
        _client = client;
        _ensureToken = ensureToken;
    }

    public async Task<IReadOnlyList<Template>> ExecuteAsync(bool invoiceWithCode, CancellationToken ct)
    {
        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var all = await _client.ListTemplatesAsync(invoiceWithCode, ct).ConfigureAwait(false);
        var active = new List<Template>(all.Count);
        foreach (var t in all)
        {
            if (t.IsActive) active.Add(t);
        }
        return active;
    }
}
