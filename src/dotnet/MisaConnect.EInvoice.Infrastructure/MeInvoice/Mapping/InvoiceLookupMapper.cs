using System.Globalization;
using MisaConnect.EInvoice.Application.Mapping;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Wire;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice.Mapping;

/// <summary>
/// Wire ↔ Application translation for slice 5 lookup operations
/// (R-LU-20). String status fields are coerced to <c>int?</c>;
/// <c>InvDate</c> parses as <c>DateTime?</c> in <c>"yyyy-MM-dd"</c>;
/// <c>CreatedDate</c> / <c>ModifiedDate</c> assume <c>+07:00</c> when MISA
/// returns a local-time string with no offset.
/// </summary>
internal static class InvoiceLookupMapper
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static InvoiceSnapshot FromWire(InvoiceDataLookupDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var refIdValue = string.IsNullOrEmpty(dto.RefID) ? string.Empty : dto.RefID;
        var refId = string.IsNullOrEmpty(refIdValue) ? default : RefId.From(refIdValue);

        RefId? orgRefId = string.IsNullOrEmpty(dto.OrgRefID) ? null : RefId.From(dto.OrgRefID);

        int? rawEInvoiceStatus = TryParseInt(dto.EInvoiceStatus);
        int? rawPublishStatus = TryParseInt(dto.PublishStatus);

        var status = InvoiceStatusMapper.Map(rawEInvoiceStatus, rawPublishStatus);

        return new InvoiceSnapshot(
            RefId: refId,
            InvoiceTemplateID: dto.InvoiceTemplateID,
            InvSeries: dto.InvSeries,
            InvDate: TryParseDate(dto.InvDate),
            InvNo: dto.InvNo,
            AccountObjectTaxCode: dto.AccountObjectTaxCode,
            AccountObjectName: dto.AccountObjectName,
            TotalSaleAmount: dto.TotalSaleAmount,
            TotalVATAmount: dto.TotalVATAmount,
            TotalAmount: dto.TotalAmount,
            TotalSaleAmountOC: dto.TotalSaleAmountOC,
            TotalVATAmountOC: dto.TotalVATAmountOC,
            TotalAmountOC: dto.TotalAmountOC,
            RawEInvoiceStatus: rawEInvoiceStatus,
            RawPublishStatus: rawPublishStatus,
            Status: status,
            OrgRefID: orgRefId,
            CreatedDate: TryParseLocalDateTime(dto.CreatedDate),
            ModifiedDate: TryParseLocalDateTime(dto.ModifiedDate));
    }

    public static PagedLookupRequestDto ToWire(PagedLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PagedLookupRequestDto(
            Start: request.Start,
            Length: request.Length,
            Sort: request.Sort,
            FromDate: request.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate: request.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PublishStatus: request.PublishStatus?.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }
        return null;
    }

    private static DateTimeOffset? TryParseLocalDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        // If the string carries an explicit offset or 'Z' (UTC), honour it.
        var hasOffset = value.EndsWith('Z')
            || value.Contains('+', StringComparison.Ordinal)
            || (value.Length >= 6 && value[^3] == ':' && (value[^6] == '+' || value[^6] == '-'));

        if (hasOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset;
        }

        // Local time without offset → assume Vietnam +07:00 (R-LU-20).
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, VietnamOffset);
        }

        return null;
    }
}
