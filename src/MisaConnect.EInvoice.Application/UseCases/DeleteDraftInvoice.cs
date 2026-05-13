using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Errors;
using Microsoft.Extensions.Logging;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class DeleteDraftInvoice
{
    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly InvalidTransactionDisambiguator _disambiguator;
    private readonly IDeleteOptionsAccessor _options;
    private readonly ILogger<DeleteDraftInvoice> _logger;

    public DeleteDraftInvoice(
        IMeInvoiceClient client,
        EnsureAccessToken ensureToken,
        InvalidTransactionDisambiguator disambiguator,
        IDeleteOptionsAccessor options,
        ILogger<DeleteDraftInvoice> logger)
    {
        _client = client;
        _ensureToken = ensureToken;
        _disambiguator = disambiguator;
        _options = options;
        _logger = logger;
    }

    public async Task<DeleteDraftOutcome> ExecuteAsync(DeleteDraftRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (MeInvoiceException ex)
        {
            return ToOutcome(ex, request);
        }

        DeleteResponse response;
        try
        {
            response = await _client.DeleteDraftAsync(request.RefId, request.InvoiceWithCode, ct).ConfigureAwait(false);
        }
        catch (MeInvoiceException ex)
        {
            return ToOutcome(ex, request);
        }

        if (response.Success)
        {
            return new DeleteDraftOutcome(
                Status: DeleteDraftStatus.Deleted,
                RawErrorCode: null,
                Message: null,
                Field: null,
                RefId: request.RefId);
        }

        var includeMessage = _options.IncludeRawErrorMessage;
        var rawCode = response.ErrorCode ?? string.Empty;

        if (string.Equals(rawCode, "InvalidTransactionID", StringComparison.OrdinalIgnoreCase))
        {
            var classification = _disambiguator.Classify(response.ErrorMessage);
            if (classification == MeInvoiceErrorCategory.NotDeletable)
            {
                return new DeleteDraftOutcome(
                    Status: DeleteDraftStatus.NotDeletable,
                    RawErrorCode: rawCode,
                    Message: includeMessage ? response.ErrorMessage : null,
                    Field: null,
                    RefId: request.RefId);
            }

            // classification == ResourceNotFound — emit drift-detection log unless
            // the message hit the canonical "không tồn tại" allow-list match.
            EmitDisambiguationWarningIfNeeded(response.ErrorMessage, includeMessage);

            return new DeleteDraftOutcome(
                Status: DeleteDraftStatus.NotFound,
                RawErrorCode: rawCode,
                Message: includeMessage ? response.ErrorMessage : null,
                Field: null,
                RefId: request.RefId);
        }

        var mapped = MeInvoiceErrorMapper.Map(response.ErrorCode, response.ErrorMessage);
        return ToOutcome(mapped, request, response.ErrorMessage, includeMessage);
    }

    private DeleteDraftOutcome ToOutcome(MeInvoiceException ex, DeleteDraftRequest request)
    {
        var includeMessage = _options.IncludeRawErrorMessage;
        var mapped = new MeInvoiceErrorCode(ex.Category, ex.RawErrorCode ?? string.Empty, ex.Message, ex.Field, ex.Component);
        return ToOutcome(mapped, request, ex.Message, includeMessage);
    }

    private static DeleteDraftOutcome ToOutcome(
        MeInvoiceErrorCode mapped,
        DeleteDraftRequest request,
        string? rawMessage,
        bool includeMessage)
    {
        var status = mapped.Category switch
        {
            MeInvoiceErrorCategory.Authentication => DeleteDraftStatus.AuthFailed,
            MeInvoiceErrorCategory.Configuration => DeleteDraftStatus.Configuration,
            MeInvoiceErrorCategory.MisaThrottled => DeleteDraftStatus.MisaThrottled,
            MeInvoiceErrorCategory.MisaUnavailable => DeleteDraftStatus.MisaUnavailable,
            MeInvoiceErrorCategory.TransportFailed => DeleteDraftStatus.TransportFailed,
            MeInvoiceErrorCategory.ResourceNotFound => DeleteDraftStatus.NotFound,
            MeInvoiceErrorCategory.NotDeletable => DeleteDraftStatus.NotDeletable,
            _ => DeleteDraftStatus.MisaUnknown,
        };

        return new DeleteDraftOutcome(
            Status: status,
            RawErrorCode: string.IsNullOrEmpty(mapped.RawCode) ? null : mapped.RawCode,
            Message: includeMessage ? rawMessage : null,
            Field: mapped.Field,
            RefId: request.RefId);
    }

    private void EmitDisambiguationWarningIfNeeded(string? errorMessage, bool includeMessage)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage) &&
            errorMessage.Contains("không tồn tại", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string EventId = "ARCH-DEL-001";
        string? excerpt = null;
        if (!string.IsNullOrEmpty(errorMessage) && includeMessage)
        {
            excerpt = errorMessage.Length > 80 ? errorMessage[..80] : errorMessage;
        }

        _logger.LogWarning(
            "MeInvoice delete disambiguation default-branch: eventId={EventId}, errorCode={ErrorCode}, errorMessageExcerpt={ErrorMessageExcerpt}",
            EventId,
            "InvalidTransactionID",
            excerpt);
    }
}
