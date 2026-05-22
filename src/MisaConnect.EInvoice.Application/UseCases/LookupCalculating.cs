using Microsoft.Extensions.Logging;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Thin orchestrator (R-LU-11) for paged lookup of POS-calculating-template
/// (<c>InvSeries[4] == 'M'</c>) invoices via
/// <c>POST /webapp/paging/calculating</c>. Code duplication versus
/// <see cref="LookupStandard"/> is intentional — the two operations are
/// deliberately distinct on the port and in the operation manifest per
/// FR-046 / R-LU-02.
/// </summary>
public sealed class LookupCalculating
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly ILogger<LookupCalculating> _logger;

    public LookupCalculating(
        IMeInvoiceClient client,
        EnsureAccessToken ensureToken,
        ILogger<LookupCalculating> logger)
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
        return await _client.LookupCalculatingAsync(request, invoiceWithCode, ct).ConfigureAwait(false);
    }
}
