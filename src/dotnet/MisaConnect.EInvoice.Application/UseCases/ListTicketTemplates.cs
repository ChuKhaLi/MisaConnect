using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Templates;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Lists e-ticket templates via <c>POST /webapp/templates/ticket</c>. Validates the paging
/// request before any network call. Rows are returned as MISA sends them (no active-only filter:
/// the endpoint is paged, so filtering would shorten pages and break <c>start</c> arithmetic).
/// </summary>
public sealed class ListTicketTemplates
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;

    public ListTicketTemplates(IMeInvoiceClient client, EnsureAccessToken ensureToken)
    {
        _client = client;
        _ensureToken = ensureToken;
    }

    public async Task<IReadOnlyList<TicketTemplate>> ExecuteAsync(TicketTemplateListRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var failure = request.Validate();
        if (failure is not null)
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                rawErrorCode: failure.RuleId,
                message: failure.Message,
                field: failure.FieldPath,
                failures: new[] { failure });
        }

        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        return await _client.ListTicketTemplatesAsync(request, ct).ConfigureAwait(false);
    }
}
