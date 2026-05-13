using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using Microsoft.Extensions.Logging;

namespace MisaConnect.EInvoice.Application.UseCases;

/// <summary>
/// Batch lookup by RefID with transparent chunking and the FR-044 cascade
/// rule (R-LU-10). Sequential <c>await</c> in a loop; never <c>Task.WhenAll</c>.
/// Caller input order is preserved via dictionary-keyed reorder against
/// MISA's response.
/// </summary>
public sealed class LookupByRefIds
{
    public const int MaxBatchSize = 50;

    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;
    private readonly IDeleteOptionsAccessor _options;
    private readonly ILogger<LookupByRefIds> _logger;

    public LookupByRefIds(
        IMeInvoiceClient client,
        EnsureAccessToken ensureToken,
        IDeleteOptionsAccessor options,
        ILogger<LookupByRefIds> logger)
    {
        _client = client;
        _ensureToken = ensureToken;
        _options = options;
        _logger = logger;
    }

    public async Task<LookupBatchOutcome> ExecuteAsync(LookupByRefIdRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationFailure = request.Validate();
        if (validationFailure is not null)
        {
            return new LookupBatchOutcome(
                Status: LookupBatchStatus.Validation,
                Outcomes: null,
                Reason: validationFailure);
        }

        try
        {
            await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        }
        catch (MeInvoiceException ex)
        {
            // A token-acquisition failure cascades as a terminal failure to every RefID.
            var category = CategoryToLookupStatus(ex.Category);
            return new LookupBatchOutcome(
                Status: LookupBatchStatus.Completed,
                Outcomes: SpreadTerminal(request.RefIds, category, ex.RawErrorCode ?? string.Empty, ex.Message),
                Reason: null);
        }

        var includeMessage = _options.IncludeRawErrorMessage;
        var refIds = request.RefIds;
        var outcomes = new LookupOutcome[refIds.Count];

        LookupStatus? terminal = null;
        string? terminalRawCode = null;
        string? terminalMessage = null;

        for (var offset = 0; offset < refIds.Count; offset += MaxBatchSize)
        {
            var size = Math.Min(MaxBatchSize, refIds.Count - offset);
            var chunk = new RefId[size];
            for (var i = 0; i < size; i++) chunk[i] = refIds[offset + i];

            if (terminal is { } term)
            {
                for (var i = 0; i < size; i++)
                {
                    outcomes[offset + i] = new LookupOutcome(
                        Status: term,
                        Snapshot: null,
                        RawErrorCode: terminalRawCode,
                        Message: includeMessage ? terminalMessage : null,
                        RefId: chunk[i]);
                }
                continue;
            }

            LookupByRefIdChunkResult chunkResult;
            try
            {
                chunkResult = await _client.LookupByRefIdAsync(chunk, request.InvoiceWithCode, ct).ConfigureAwait(false);
            }
            catch (MeInvoiceException ex)
            {
                var category = CategoryToLookupStatus(ex.Category);
                FillChunkWithError(outcomes, offset, chunk, category, ex.RawErrorCode ?? string.Empty, ex.Message, includeMessage);
                if (IsTerminal(category))
                {
                    terminal = category;
                    terminalRawCode = ex.RawErrorCode ?? string.Empty;
                    terminalMessage = ex.Message;
                }
                continue;
            }

            if (!string.IsNullOrEmpty(chunkResult.ErrorCode))
            {
                var mapped = MeInvoiceErrorMapper.Map(chunkResult.ErrorCode, chunkResult.ErrorMessage);
                var category = CategoryToLookupStatus(mapped.Category);
                FillChunkWithError(outcomes, offset, chunk, category, chunkResult.ErrorCode!, chunkResult.ErrorMessage, includeMessage);
                if (IsTerminal(category))
                {
                    terminal = category;
                    terminalRawCode = chunkResult.ErrorCode;
                    terminalMessage = chunkResult.ErrorMessage;
                }
                continue;
            }

            // Success: index snapshots by RefID, then reorder per chunk input.
            var byRefId = new Dictionary<string, InvoiceSnapshot>(chunkResult.Snapshots.Count, StringComparer.Ordinal);
            foreach (var snap in chunkResult.Snapshots)
            {
                if (!string.IsNullOrEmpty(snap.RefId.Value))
                {
                    byRefId[snap.RefId.Value] = snap;
                }
            }

            for (var i = 0; i < size; i++)
            {
                var current = chunk[i];
                if (byRefId.TryGetValue(current.Value, out var snap))
                {
                    outcomes[offset + i] = new LookupOutcome(
                        Status: LookupStatus.Found,
                        Snapshot: snap,
                        RawErrorCode: null,
                        Message: null,
                        RefId: current);
                }
                else
                {
                    outcomes[offset + i] = new LookupOutcome(
                        Status: LookupStatus.NotFound,
                        Snapshot: null,
                        RawErrorCode: null,
                        Message: null,
                        RefId: current);
                }
            }
        }

        return new LookupBatchOutcome(
            Status: LookupBatchStatus.Completed,
            Outcomes: outcomes,
            Reason: null);
    }

    private static bool IsTerminal(LookupStatus status) =>
        status is LookupStatus.AuthFailed or LookupStatus.Configuration;

    private static void FillChunkWithError(
        LookupOutcome[] outcomes,
        int offset,
        IReadOnlyList<RefId> chunk,
        LookupStatus status,
        string rawErrorCode,
        string? message,
        bool includeMessage)
    {
        for (var i = 0; i < chunk.Count; i++)
        {
            outcomes[offset + i] = new LookupOutcome(
                Status: status,
                Snapshot: null,
                RawErrorCode: string.IsNullOrEmpty(rawErrorCode) ? null : rawErrorCode,
                Message: includeMessage ? message : null,
                RefId: chunk[i]);
        }
    }

    private static IReadOnlyList<LookupOutcome> SpreadTerminal(
        IReadOnlyList<RefId> refIds,
        LookupStatus status,
        string rawErrorCode,
        string? message)
    {
        var list = new List<LookupOutcome>(refIds.Count);
        foreach (var r in refIds)
        {
            list.Add(new LookupOutcome(
                Status: status,
                Snapshot: null,
                RawErrorCode: string.IsNullOrEmpty(rawErrorCode) ? null : rawErrorCode,
                Message: null,
                RefId: r));
        }
        return list;
    }

    private static LookupStatus CategoryToLookupStatus(MeInvoiceErrorCategory category) => category switch
    {
        MeInvoiceErrorCategory.Authentication => LookupStatus.AuthFailed,
        MeInvoiceErrorCategory.Configuration => LookupStatus.Configuration,
        MeInvoiceErrorCategory.MisaThrottled => LookupStatus.MisaThrottled,
        MeInvoiceErrorCategory.MisaUnavailable => LookupStatus.MisaUnavailable,
        MeInvoiceErrorCategory.TransportFailed => LookupStatus.TransportFailed,
        MeInvoiceErrorCategory.ResourceNotFound => LookupStatus.NotFound,
        MeInvoiceErrorCategory.Validation => LookupStatus.Validation,
        _ => LookupStatus.MisaUnknown,
    };
}
