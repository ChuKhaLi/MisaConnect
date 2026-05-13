using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client.Dtos;
using MisaConnect.EInvoice.Client.Mapping;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.Samples.Api.Endpoints;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/api/invoices/preview", async (
            InvoiceDto invoice,
            PreviewInvoice useCase,
            HttpContext context,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                var domain = DtoMapper.ToDomain(invoice);
                var result = await useCase.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
                context.Response.Headers.ContentDisposition = "inline; filename=\"preview.pdf\"";
                return Results.File(result.Pdf.Content, "application/pdf");
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        builder.MapPost("/api/invoices/draft", async (
            IReadOnlyList<InvoiceDto> invoices,
            SaveDraftInvoices useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                var domain = new List<Invoice>(invoices.Count);
                foreach (var dto in invoices) domain.Add(DtoMapper.ToDomain(dto));
                var results = await useCase.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
                var list = new List<object>(results.Count);
                foreach (var r in results)
                {
                    list.Add(new
                    {
                        refId = r.RefId.Value,
                        outcome = r.Outcome.ToString(),
                        errorCategory = r.Error?.Category.ToString(),
                        rawErrorCode = r.Error?.RawCode,
                        message = r.Error?.Detail,
                        failures = r.LocalFailures?.Select(f => new
                        {
                            fieldPath = f.FieldPath,
                            message = f.Message,
                            ruleId = f.RuleId,
                        }).ToArray(),
                    });
                }
                return Results.Ok(new { results = list });
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        builder.MapGet("/api/invoices/{refId}/pdf", async (
            string refId,
            GetDraftPdfByRefId useCase,
            HttpContext context,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                var decoded = Uri.UnescapeDataString(refId);
                var pdf = await useCase.ExecuteAsync(decoded, withCode, ct).ConfigureAwait(false);
                context.Response.Headers.ContentDisposition = $"inline; filename=\"invoice-{decoded}.pdf\"";
                return Results.File(pdf.Content, "application/pdf");
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        builder.MapDelete("/api/invoices/{refId}", async (
            string refId,
            DeleteDraftInvoice useCase,
            bool? withCode,
            CancellationToken ct) =>
        {
            if (withCode is null)
            {
                return Results.BadRequest(new { error = "withCode query parameter is required" });
            }

            RefId parsed;
            try
            {
                parsed = RefId.From(Uri.UnescapeDataString(refId));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var outcome = await useCase.ExecuteAsync(new DeleteDraftRequest(parsed, withCode.Value), ct).ConfigureAwait(false);
            return DeleteOutcomeMapping.ToResult(outcome);
        });

        // === Slice 5: read-only lookup endpoints ===

        builder.MapPost("/api/invoices/lookup/by-refid", async (
            IReadOnlyList<string> refIds,
            LookupByRefIds useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            if (refIds is null || refIds.Count == 0)
            {
                return Results.Json(
                    new { status = "Validation", reason = "RefIds list cannot be empty." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var parsed = new List<RefId>(refIds.Count);
            foreach (var s in refIds)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    return Results.Json(
                        new { status = "Validation", reason = "RefId value must be non-empty." },
                        statusCode: StatusCodes.Status400BadRequest);
                }
                try
                {
                    parsed.Add(RefId.From(s));
                }
                catch (ArgumentException ex)
                {
                    return Results.Json(
                        new { status = "Validation", reason = ex.Message },
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            try
            {
                var outcome = await useCase.ExecuteAsync(new LookupByRefIdRequest(parsed, withCode), ct).ConfigureAwait(false);
                return LookupBatchOutcomeMapping.ToResult(outcome);
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        builder.MapPost("/api/invoices/lookup/standard", async (
            PagedLookupRequestDto request,
            LookupStandard useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                var domain = LookupRequestMapping.ToDomain(request);
                var result = await useCase.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
                return Results.Ok(LookupRequestMapping.ToDto(result));
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        builder.MapPost("/api/invoices/lookup/calculating", async (
            PagedLookupRequestDto request,
            LookupCalculating useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                var domain = LookupRequestMapping.ToDomain(request);
                var result = await useCase.ExecuteAsync(domain, withCode, ct).ConfigureAwait(false);
                return Results.Ok(LookupRequestMapping.ToDto(result));
            }
            catch (MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }
        });

        // === Slice 6: invoice amendment endpoints ===

        builder.MapPost("/api/invoices/replacement", async (
            ReplacementRequestDto request,
            IssueReplacementInvoice useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                if (request is null)
                {
                    return Results.BadRequest(new { error = "Request body is required." });
                }
                var domain = AmendmentDtoMapper.ToDomain(request);
                var result = await useCase.ExecuteAsync(domain, ct).ConfigureAwait(false);
                return AmendmentResultMapping.ToResult(result, request.OriginalRef.OrgRefID);
            }
            catch (MeInvoiceException ex)
            {
                return AmendmentResultMapping.ToErrorResult(ex, request?.OriginalRef?.OrgRefID);
            }
        });

        builder.MapPost("/api/invoices/adjustment", async (
            AdjustmentRequestDto request,
            IssueAdjustmentInvoice useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            try
            {
                if (request is null)
                {
                    return Results.BadRequest(new { error = "Request body is required." });
                }
                var domain = AmendmentDtoMapper.ToDomain(request);
                var result = await useCase.ExecuteAsync(domain, ct).ConfigureAwait(false);
                return AmendmentResultMapping.ToResult(result, request.OriginalRef.OrgRefID);
            }
            catch (MeInvoiceException ex)
            {
                return AmendmentResultMapping.ToErrorResult(ex, request?.OriginalRef?.OrgRefID);
            }
        });

        return builder;
    }
}

internal static class AmendmentResultMapping
{
    public static IResult ToResult(SaveResult result, string orgRefId)
    {
        var dto = AmendmentDtoMapper.ToDto(result, orgRefId);
        var statusCode = StatusCodeFor(result);
        return Results.Json(dto, statusCode: statusCode);
    }

    public static IResult ToErrorResult(MeInvoiceException ex, string? orgRefId)
    {
        var statusCode = StatusCodeForCategory(ex.Category);
        var body = new AmendmentResultDto(
            RefId: ex.RefId ?? string.Empty,
            Outcome: SaveOutcomeDto.Error,
            ErrorCategory: ex.Category.ToString(),
            RawErrorCode: ex.RawErrorCode,
            ErrorMessage: ex.Message,
            Failures: ex.Failures?.Select(f => new ValidationFailureDto(f.FieldPath, f.Message, f.RuleId)).ToArray(),
            OrgRefId: orgRefId);
        return Results.Json(body, statusCode: statusCode);
    }

    private static int StatusCodeFor(SaveResult result)
    {
        if (result.Outcome == SaveOutcome.Success) return StatusCodes.Status200OK;
        return StatusCodeForCategory(result.Error?.Category ?? MeInvoiceErrorCategory.MisaUnknown);
    }

    private static int StatusCodeForCategory(MeInvoiceErrorCategory category) => category switch
    {
        MeInvoiceErrorCategory.Replacement => StatusCodes.Status409Conflict,
        MeInvoiceErrorCategory.DuplicateOrUniqueness => StatusCodes.Status409Conflict,
        MeInvoiceErrorCategory.Validation => StatusCodes.Status400BadRequest,
        MeInvoiceErrorCategory.Authentication => StatusCodes.Status401Unauthorized,
        MeInvoiceErrorCategory.ResourceNotFound => StatusCodes.Status404NotFound,
        MeInvoiceErrorCategory.MisaThrottled => StatusCodes.Status503ServiceUnavailable,
        MeInvoiceErrorCategory.MisaUnavailable
            or MeInvoiceErrorCategory.MisaUnknown
            or MeInvoiceErrorCategory.TransportFailed
            or MeInvoiceErrorCategory.Signing => StatusCodes.Status502BadGateway,
        MeInvoiceErrorCategory.Configuration => StatusCodes.Status400BadRequest,
        MeInvoiceErrorCategory.TemplateState => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };
}

internal static class LookupRequestMapping
{
    public static PagedLookupRequest ToDomain(PagedLookupRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var sort = string.IsNullOrWhiteSpace(dto.Sort) ? "InvDate" : dto.Sort;
        return new PagedLookupRequest(
            Start: dto.Start,
            Length: dto.Length,
            Sort: sort,
            FromDate: dto.FromDate,
            ToDate: dto.ToDate,
            PublishStatus: dto.PublishStatus);
    }

    public static PagedLookupResultDto ToDto(PagedResult result)
    {
        var items = new List<InvoiceSnapshotDto>(result.Items.Count);
        foreach (var snap in result.Items) items.Add(LookupSnapshotMapping.ToDto(snap));
        return new PagedLookupResultDto(items, result.Start, result.Length, result.ReturnedCount);
    }
}

internal static class LookupSnapshotMapping
{
    public static InvoiceSnapshotDto ToDto(InvoiceSnapshot snap) => new(
        RefId: snap.RefId.Value ?? string.Empty,
        InvoiceTemplateID: snap.InvoiceTemplateID,
        InvSeries: snap.InvSeries,
        InvDate: snap.InvDate,
        InvNo: snap.InvNo,
        AccountObjectTaxCode: snap.AccountObjectTaxCode,
        AccountObjectName: snap.AccountObjectName,
        TotalSaleAmount: snap.TotalSaleAmount,
        TotalVATAmount: snap.TotalVATAmount,
        TotalAmount: snap.TotalAmount,
        TotalSaleAmountOC: snap.TotalSaleAmountOC,
        TotalVATAmountOC: snap.TotalVATAmountOC,
        TotalAmountOC: snap.TotalAmountOC,
        RawEInvoiceStatus: snap.RawEInvoiceStatus,
        RawPublishStatus: snap.RawPublishStatus,
        Status: snap.Status.ToString(),
        OrgRefID: snap.OrgRefID?.Value,
        CreatedDate: snap.CreatedDate,
        ModifiedDate: snap.ModifiedDate);
}

internal static class LookupBatchOutcomeMapping
{
    public static IResult ToResult(LookupBatchOutcome outcome)
    {
        if (outcome.Status == LookupBatchStatus.Validation)
        {
            return Results.Json(
                new { status = "Validation", reason = outcome.Reason },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var outcomes = outcome.Outcomes ?? Array.Empty<LookupOutcome>();
        var (statusCode, retryAfterSeconds) = ChooseStatusCode(outcomes);

        var body = new
        {
            status = "Completed",
            outcomes = outcomes.Select(o => new
            {
                refId = o.RefId.Value,
                status = o.Status.ToString(),
                snapshot = o.Snapshot is null ? null : LookupSnapshotMapping.ToDto(o.Snapshot),
                errorCode = o.RawErrorCode,
                message = o.Message,
            }),
        };

        return new LookupBatchResult(body, statusCode, retryAfterSeconds);
    }

    private static (int Status, int? RetryAfterSeconds) ChooseStatusCode(IReadOnlyList<LookupOutcome> outcomes)
    {
        if (outcomes.Count == 0) return (StatusCodes.Status200OK, null);

        bool any(LookupStatus s)
        {
            foreach (var o in outcomes) if (o.Status == s) return true;
            return false;
        }

        bool all(LookupStatus s)
        {
            foreach (var o in outcomes) if (o.Status != s) return false;
            return true;
        }

        if (any(LookupStatus.Found) || any(LookupStatus.NotFound))
        {
            return (StatusCodes.Status200OK, null);
        }
        if (all(LookupStatus.AuthFailed)) return (StatusCodes.Status401Unauthorized, null);
        if (all(LookupStatus.Configuration)) return (StatusCodes.Status400BadRequest, null);
        if (all(LookupStatus.MisaThrottled)) return (StatusCodes.Status503ServiceUnavailable, 2);
        if (all(LookupStatus.MisaUnavailable)
            || all(LookupStatus.TransportFailed)
            || all(LookupStatus.MisaUnknown))
        {
            return (StatusCodes.Status502BadGateway, null);
        }
        // Mixed-error response (e.g. throttle + unknown): 502 is the safest catch-all.
        return (StatusCodes.Status502BadGateway, null);
    }

    private sealed class LookupBatchResult : IResult
    {
        private readonly object _body;
        private readonly int _status;
        private readonly int? _retryAfter;

        public LookupBatchResult(object body, int status, int? retryAfter)
        {
            _body = body;
            _status = status;
            _retryAfter = retryAfter;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (_retryAfter is not null)
            {
                httpContext.Response.Headers["Retry-After"] = _retryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Results.Json(_body, statusCode: _status).ExecuteAsync(httpContext);
        }
    }
}

internal static class DeleteOutcomeMapping
{
    public static IResult ToResult(DeleteDraftOutcome outcome)
    {
        var (status, retryAfter) = MapStatus(outcome.Status);
        var body = new
        {
            refId = outcome.RefId?.Value,
            status = outcome.Status.ToString(),
            errorCode = outcome.RawErrorCode,
            field = outcome.Field,
            message = outcome.Message,
        };

        return new DeleteResult(body, status, retryAfter);
    }

    private static (int Status, int? RetryAfterSeconds) MapStatus(DeleteDraftStatus status) => status switch
    {
        DeleteDraftStatus.Deleted => (StatusCodes.Status200OK, (int?)null),
        DeleteDraftStatus.NotFound => (StatusCodes.Status404NotFound, null),
        DeleteDraftStatus.NotDeletable => (StatusCodes.Status409Conflict, null),
        DeleteDraftStatus.AuthFailed => (StatusCodes.Status401Unauthorized, null),
        DeleteDraftStatus.Configuration => (StatusCodes.Status400BadRequest, null),
        DeleteDraftStatus.MisaThrottled => (StatusCodes.Status503ServiceUnavailable, 2),
        DeleteDraftStatus.MisaUnavailable => (StatusCodes.Status502BadGateway, null),
        DeleteDraftStatus.TransportFailed => (StatusCodes.Status502BadGateway, null),
        DeleteDraftStatus.MisaUnknown => (StatusCodes.Status502BadGateway, null),
        _ => (StatusCodes.Status502BadGateway, null),
    };

    private sealed class DeleteResult : IResult
    {
        private readonly object _body;
        private readonly int _status;
        private readonly int? _retryAfter;

        public DeleteResult(object body, int status, int? retryAfter)
        {
            _body = body;
            _status = status;
            _retryAfter = retryAfter;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (_retryAfter is not null)
            {
                httpContext.Response.Headers["Retry-After"] = _retryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return Results.Json(_body, statusCode: _status).ExecuteAsync(httpContext);
        }
    }
}

internal static class ErrorMapping
{
    public static IResult ToResult(MeInvoiceException ex)
    {
        var status = ex.Category switch
        {
            MeInvoiceErrorCategory.Authentication => StatusCodes.Status401Unauthorized,
            MeInvoiceErrorCategory.ResourceNotFound => StatusCodes.Status404NotFound,
            MeInvoiceErrorCategory.MisaThrottled => StatusCodes.Status503ServiceUnavailable,
            MeInvoiceErrorCategory.MisaUnavailable
                or MeInvoiceErrorCategory.MisaUnknown
                or MeInvoiceErrorCategory.Signing => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };

        var body = new
        {
            errorCategory = ex.Category.ToString(),
            rawErrorCode = ex.RawErrorCode,
            message = ex.Message,
            field = ex.Field,
            component = ex.Component,
            refId = ex.RefId,
            failures = ex.Failures?.Select(f => new { fieldPath = f.FieldPath, message = f.Message, ruleId = f.RuleId }).ToArray(),
        };

        return Results.Json(body, statusCode: status);
    }
}
