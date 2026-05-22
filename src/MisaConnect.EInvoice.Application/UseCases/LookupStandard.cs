using Microsoft.Extensions.Logging;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Thin orchestrator (R-LU-11) for paged lookup of standard-template
/// (<c>InvSeries[4] == 'T'</c>) invoices via <c>POST /webapp/paging</c>.
/// </summary>
public sealed class LookupStandard
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly ILogger<LookupStandard> _logger;

    public LookupStandard(
        IMeInvoiceClient client,
        EnsureAccessToken ensureToken,
        ILogger<LookupStandard> logger)
    {
        _client = client;
        _ensureToken = ensureToken;
        _logger = logger;
    }

    public async Task<PagedResult> ExecuteAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
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
        return await _client.LookupStandardAsync(request, invoiceWithCode, ct).ConfigureAwait(false);
    }
}
