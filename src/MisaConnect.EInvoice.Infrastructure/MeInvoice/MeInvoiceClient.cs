using System.Net.Http.Json;
using System.Text.Json;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Auth;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;
using Microsoft.Extensions.Options;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice;

/// <summary>
/// Sole concrete implementation of <see cref="IMeInvoiceClient"/>. Talks to
/// MISA's five integration endpoints and unwraps the project-specific
/// stringified-JSON envelope: every MISA response is shaped as
/// <c>{success, data: "<json string>", error: "<json string|string>", errorCode}</c>.
/// </summary>
internal sealed class MeInvoiceClient : IMeInvoiceClient
{
    private readonly HttpClient _http;
    private readonly IOptions<MisaEInvoiceOptions> _options;

    public MeInvoiceClient(HttpClient http, IOptions<MisaEInvoiceOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
    {
        var opts = _options.Value;
        var request = new TokenRequestDto(
            taxcode: WireTaxCode.Compose(opts),
            username: opts.UserName,
            password: opts.Password);

        var envelope = await PostEnvelopeAsync(MeInvoiceHttpRoutes.Token, request, ct).ConfigureAwait(false);

        if (!envelope.success || string.IsNullOrEmpty(envelope.data))
        {
            ThrowFromEnvelope(envelope);
        }

        var inner = JsonSerializer.Deserialize<TokenInnerDto>(envelope.data!, MeInvoiceJsonOptions.Wire)
            ?? throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnknown, "EmptyResponse", "Token response had no inner payload.");

        if (string.IsNullOrEmpty(inner.access_token))
        {
            throw new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "EmptyToken", "MISA returned a token response with no access_token.");
        }

        return new AccessToken(inner.access_token, DateTimeOffset.UtcNow.AddDays(14));
    }

    public async Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct)
    {
        var opts = _options.Value;
        var body = new TemplateRequestDto(
            TaxCode: WireTaxCode.Compose(opts),
            UserName: opts.UserName,
            Password: opts.Password);

        var url = $"{MeInvoiceHttpRoutes.Templates}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}&isTicket=false";
        var envelope = await PostEnvelopeAsync(url, body, ct).ConfigureAwait(false);

        if (!envelope.success)
        {
            ThrowFromEnvelope(envelope);
        }

        if (string.IsNullOrEmpty(envelope.data))
        {
            return Array.Empty<Template>();
        }

        var dtos = JsonSerializer.Deserialize<IReadOnlyList<TemplateDto>>(envelope.data, MeInvoiceJsonOptions.Wire)
            ?? Array.Empty<TemplateDto>();

        var output = new List<Template>(dtos.Count);
        foreach (var dto in dtos)
        {
            output.Add(InvoiceDtoMapper.FromWire(dto, defaultWithCode: invoiceWithCode));
        }
        return output;
    }

    public async Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct)
    {
        var body = InvoiceDtoMapper.ToWire(invoice);
        var url = $"{MeInvoiceHttpRoutes.Preview}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}";
        var envelope = await PostEnvelopeAsync(url, body, ct).ConfigureAwait(false);

        if (!envelope.success || string.IsNullOrEmpty(envelope.data))
        {
            ThrowFromEnvelope(envelope, refId: invoice.RefId.Value);
        }

        var bytes = Convert.FromBase64String(envelope.data!);
        return new PdfDocument(bytes);
    }

    public async Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct)
    {
        var body = new List<InvoiceDataDto>(batch.Invoices.Count);
        foreach (var inv in batch.Invoices)
        {
            body.Add(InvoiceDtoMapper.ToWire(inv));
        }

        var keyedRefIds = new List<string>(batch.Invoices.Count);
        foreach (var inv in batch.Invoices) keyedRefIds.Add(inv.RefId.Value);

        return await PostInsertAsync(body, keyedRefIds, invoiceWithCode, ct).ConfigureAwait(false);
    }

    public async Task<SaveResult> IssueReplacementAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(originalRef);

        var wireRef = ToWire(originalRef);
        var dto = InvoiceDtoMapper.ToWire(invoice, "3", wireRef, changeReason);
        var results = await PostInsertAsync(
            new[] { dto },
            new[] { invoice.RefId.Value },
            invoiceWithCode,
            ct).ConfigureAwait(false);
        return results[0];
    }

    public async Task<SaveResult> IssueAdjustmentAsync(
        Invoice invoice,
        OriginalInvoiceReference originalRef,
        string changeReason,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(originalRef);

        var wireRef = ToWire(originalRef);
        var dto = InvoiceDtoMapper.ToWire(invoice, "4", wireRef, changeReason);
        var results = await PostInsertAsync(
            new[] { dto },
            new[] { invoice.RefId.Value },
            invoiceWithCode,
            ct).ConfigureAwait(false);
        return results[0];
    }

    private static OriginalInvoiceReferenceWire ToWire(OriginalInvoiceReference r) =>
        new(
            OrgRefID: r.OrgRefID,
            OrgInvNo: r.OrgInvNo,
            OrgInvTemplateNo: r.OrgInvTemplateNo,
            OrgInvSeries: r.OrgInvSeries,
            OrgInvDate: r.OrgInvDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    private async Task<IReadOnlyList<SaveResult>> PostInsertAsync(
        IReadOnlyList<InvoiceDataDto> dtos,
        IReadOnlyList<string> orderedRefIds,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        var url = $"{MeInvoiceHttpRoutes.Insert}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}";
        var envelope = await PostEnvelopeAsync(url, dtos, ct).ConfigureAwait(false);

        var successEntries = string.IsNullOrEmpty(envelope.data)
            ? Array.Empty<InsertSuccessEntryDto>()
            : JsonSerializer.Deserialize<IReadOnlyList<InsertSuccessEntryDto>>(envelope.data, MeInvoiceJsonOptions.Wire) ?? Array.Empty<InsertSuccessEntryDto>();

        var errorEntries = ParseErrorEntries(envelope.error);

        if (!envelope.success && successEntries.Count == 0 && errorEntries.Count == 0)
        {
            ThrowFromEnvelope(envelope);
        }

        var results = new Dictionary<string, SaveResult>(StringComparer.Ordinal);
        foreach (var entry in successEntries)
        {
            if (string.IsNullOrEmpty(entry.RefID)) continue;
            results[entry.RefID] = new SaveResult(RefId.From(entry.RefID), SaveOutcome.Success);
        }
        foreach (var entry in errorEntries)
        {
            if (string.IsNullOrEmpty(entry.RefID)) continue;
            var code = MeInvoiceErrorMapper.Map(entry.ErrorCode, entry.ErrorMessage);
            results[entry.RefID] = new SaveResult(RefId.From(entry.RefID), SaveOutcome.Error, code);
        }

        var ordered = new List<SaveResult>(orderedRefIds.Count);
        foreach (var refIdValue in orderedRefIds)
        {
            if (results.TryGetValue(refIdValue, out var r))
            {
                ordered.Add(r);
            }
            else
            {
                ordered.Add(new SaveResult(
                    RefId.From(refIdValue),
                    SaveOutcome.Error,
                    new MeInvoiceErrorCode(MeInvoiceErrorCategory.MisaUnknown, "NoEchoedResult")));
            }
        }
        return ordered;
    }

    public async Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
    {
        var url = $"{MeInvoiceHttpRoutes.DeleteDraft}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}&refid={Uri.EscapeDataString(refId.Value)}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<DeleteResponseDto>(stream, MeInvoiceJsonOptions.Wire, ct).ConfigureAwait(false);
        if (dto is null)
        {
            throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnknown, "EmptyResponse", "MISA returned an empty delete response body.");
        }

        if (dto.success)
        {
            return new DeleteResponse(Success: true, ErrorCode: null, ErrorMessage: null);
        }

        var errorCode = ExtractRawErrorCodeJson(dto.errorCode);
        return new DeleteResponse(Success: false, ErrorCode: errorCode, ErrorMessage: dto.ErrorMessage);
    }

    private static string? ExtractRawErrorCodeJson(JsonElement? code)
    {
        if (code is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].ValueKind == JsonValueKind.String ? value[0].GetString() : value[0].GetRawText(),
            _ => null,
        };
    }

    public async Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
        IReadOnlyList<RefId> refIds,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(refIds);

        var body = new string[refIds.Count];
        for (var i = 0; i < refIds.Count; i++) body[i] = refIds[i].Value;

        var url = $"{MeInvoiceHttpRoutes.LookupByRefId}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}";
        var envelope = await PostLookupEnvelopeAsync(url, body, ct).ConfigureAwait(false);

        if (!envelope.success)
        {
            return new LookupByRefIdChunkResult(
                Snapshots: Array.Empty<InvoiceSnapshot>(),
                ErrorCode: ExtractRawErrorCodeFromLookup(envelope) ?? string.Empty,
                ErrorMessage: ExtractErrorMessageFromLookup(envelope));
        }

        var snapshots = DeserializeSnapshots(envelope.data);
        return new LookupByRefIdChunkResult(snapshots, ErrorCode: null, ErrorMessage: null);
    }

    public async Task<PagedResult> LookupStandardAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await LookupPagedAsync(
            MeInvoiceHttpRoutes.LookupStandardPaging, request, invoiceWithCode, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult> LookupCalculatingAsync(
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await LookupPagedAsync(
            MeInvoiceHttpRoutes.LookupCalculatingPaging, request, invoiceWithCode, ct).ConfigureAwait(false);
    }

    private async Task<PagedResult> LookupPagedAsync(
        string route,
        PagedLookupRequest request,
        bool invoiceWithCode,
        CancellationToken ct)
    {
        var wire = InvoiceLookupMapper.ToWire(request);
        var url = $"{route}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}";
        var envelope = await PostLookupEnvelopeAsync(url, wire, ct).ConfigureAwait(false);

        if (!envelope.success)
        {
            ThrowFromLookupEnvelope(envelope);
        }

        var snapshots = DeserializeSnapshots(envelope.data);
        return new PagedResult(
            Items: snapshots,
            Start: request.Start,
            Length: request.Length,
            ReturnedCount: snapshots.Count);
    }

    private async Task<LookupEnvelopeDto> PostLookupEnvelopeAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(url, body, MeInvoiceJsonOptions.Wire, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync<LookupEnvelopeDto>(stream, MeInvoiceJsonOptions.Wire, ct).ConfigureAwait(false);
        return envelope ?? throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnknown, "EmptyResponse", "MISA returned an empty lookup response body.");
    }

    private static IReadOnlyList<InvoiceSnapshot> DeserializeSnapshots(JsonElement? data)
    {
        if (data is not { } value || value.ValueKind == JsonValueKind.Null) return Array.Empty<InvoiceSnapshot>();

        // Lookup responses return `data` as a direct JSON array per R-LU-16.
        // Some sandbox builds wrap it as a stringified JSON; handle both.
        IReadOnlyList<InvoiceDataLookupDto>? dtos = null;
        try
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Array:
                    dtos = JsonSerializer.Deserialize<IReadOnlyList<InvoiceDataLookupDto>>(
                        value.GetRawText(), MeInvoiceJsonOptions.Wire);
                    break;
                case JsonValueKind.String:
                    var raw = value.GetString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        dtos = JsonSerializer.Deserialize<IReadOnlyList<InvoiceDataLookupDto>>(
                            raw, MeInvoiceJsonOptions.Wire);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            dtos = null;
        }

        if (dtos is null || dtos.Count == 0) return Array.Empty<InvoiceSnapshot>();
        var output = new List<InvoiceSnapshot>(dtos.Count);
        foreach (var dto in dtos) output.Add(InvoiceLookupMapper.FromWire(dto));
        return output;
    }

    private static string? ExtractRawErrorCodeFromLookup(LookupEnvelopeDto envelope)
    {
        if (envelope.errorCode is not { } code) return null;
        return code.ValueKind switch
        {
            JsonValueKind.String => code.GetString(),
            JsonValueKind.Array when code.GetArrayLength() > 0 => code[0].ValueKind == JsonValueKind.String ? code[0].GetString() : code[0].GetRawText(),
            _ => null,
        };
    }

    private static string? ExtractErrorMessageFromLookup(LookupEnvelopeDto envelope)
    {
        if (envelope.error is not { } e) return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => e.GetRawText(),
        };
    }

    private static void ThrowFromLookupEnvelope(LookupEnvelopeDto envelope)
    {
        var raw = ExtractRawErrorCodeFromLookup(envelope);
        var msg = ExtractErrorMessageFromLookup(envelope);
        var code = MeInvoiceErrorMapper.Map(raw, msg);
        throw new MeInvoiceException(code.Category, code.RawCode, code.Detail ?? msg);
    }

    public async Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
    {
        var url = $"{MeInvoiceHttpRoutes.ViewRefId}?invoiceWithCode={(invoiceWithCode ? "true" : "false")}&refid={Uri.EscapeDataString(refId.Value)}";
        var envelope = await GetEnvelopeAsync(url, ct).ConfigureAwait(false);

        if (!envelope.success || string.IsNullOrEmpty(envelope.data))
        {
            // MISA returns success:true with empty data for an unknown RefID
            // (it doesn't set success:false). Treat both shapes as not-found.
            var raw = ExtractRawErrorCode(envelope) ?? "RefIdNotFound";
            var msg = ExtractErrorMessage(envelope);
            var code = MeInvoiceErrorMapper.Map(raw, msg);
            throw new MeInvoiceException(code.Category, code.RawCode, code.Detail ?? msg, refId: refId.Value);
        }

        var bytes = Convert.FromBase64String(envelope.data);
        return new PdfDocument(bytes);
    }

    private async Task<MeInvoiceEnvelopeDto> PostEnvelopeAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(url, body, MeInvoiceJsonOptions.Wire, ct).ConfigureAwait(false);
        return await ReadEnvelopeAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<MeInvoiceEnvelopeDto> GetEnvelopeAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        return await ReadEnvelopeAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<MeInvoiceEnvelopeDto> ReadEnvelopeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync<MeInvoiceEnvelopeDto>(stream, MeInvoiceJsonOptions.Wire, ct).ConfigureAwait(false);
        return envelope ?? throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnknown, "EmptyResponse", "MISA returned an empty response body.");
    }

    private static IReadOnlyList<InsertErrorEntryDto> ParseErrorEntries(JsonElement? error)
    {
        if (error is not { } value) return Array.Empty<InsertErrorEntryDto>();
        return value.ValueKind switch
        {
            JsonValueKind.String => ParseFromStringifiedJson(value.GetString()),
            JsonValueKind.Array => JsonSerializer.Deserialize<IReadOnlyList<InsertErrorEntryDto>>(value.GetRawText(), MeInvoiceJsonOptions.Wire)
                ?? Array.Empty<InsertErrorEntryDto>(),
            _ => Array.Empty<InsertErrorEntryDto>(),
        };

        static IReadOnlyList<InsertErrorEntryDto> ParseFromStringifiedJson(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return Array.Empty<InsertErrorEntryDto>();
            try
            {
                return JsonSerializer.Deserialize<IReadOnlyList<InsertErrorEntryDto>>(raw, MeInvoiceJsonOptions.Wire)
                    ?? Array.Empty<InsertErrorEntryDto>();
            }
            catch (JsonException)
            {
                // Plain string (e.g. preview's "InvoiceData is null") rather than stringified array.
                return new[] { new InsertErrorEntryDto(null, null, raw) };
            }
        }
    }

    private static string? ExtractRawErrorCode(MeInvoiceEnvelopeDto envelope)
    {
        if (envelope.errorCode is not { } code) return null;
        return code.ValueKind switch
        {
            JsonValueKind.String => code.GetString(),
            JsonValueKind.Array when code.GetArrayLength() > 0 => code[0].ValueKind == JsonValueKind.String ? code[0].GetString() : code[0].GetRawText(),
            _ => null,
        };
    }

    private static string? ExtractErrorMessage(MeInvoiceEnvelopeDto envelope)
    {
        if (envelope.error is not { } e) return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            _ => e.GetRawText(),
        };
    }

    private static void ThrowFromEnvelope(MeInvoiceEnvelopeDto envelope, string? refId = null)
    {
        var raw = ExtractRawErrorCode(envelope);
        var msg = ExtractErrorMessage(envelope);

        // Some endpoints (insert) put the actual error code into the `error`
        // string when `errorCode` is an empty array. Try harder before
        // falling back to MisaUnknown.
        if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(msg))
        {
            var errorEntries = ParseErrorEntries(envelope.error);
            if (errorEntries.Count > 0 && !string.IsNullOrEmpty(errorEntries[0].ErrorCode))
            {
                raw = errorEntries[0].ErrorCode;
            }
        }

        var code = MeInvoiceErrorMapper.Map(raw, msg);
        throw new MeInvoiceException(code.Category, code.RawCode, code.Detail ?? msg, refId: refId);
    }
}
