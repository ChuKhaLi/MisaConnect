using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed class GetDraftPdfByRefId
{
    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46, 0x2D };

    private readonly IMeInvoiceClient _client;
    private readonly EnsureAccessToken _ensureToken;

    public GetDraftPdfByRefId(IMeInvoiceClient client, EnsureAccessToken ensureToken)
    {
        _client = client;
        _ensureToken = ensureToken;
    }

    public async Task<PdfDocument> ExecuteAsync(string refId, bool invoiceWithCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refId))
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation,
                "RefIdRequired",
                "RefId must be a non-empty string.");
        }

        var typedRef = RefId.From(refId);

        await _ensureToken.ExecuteAsync(ct).ConfigureAwait(false);
        var pdf = await _client.GetDraftPdfByRefIdAsync(typedRef, invoiceWithCode, ct).ConfigureAwait(false);

        if (pdf.Content.Length < PdfMagic.Length ||
            !pdf.Content.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic))
        {
            throw new MeInvoiceException(
                MeInvoiceErrorCategory.MisaUnknown,
                "InvalidPdfContent",
                "Response did not contain a PDF document.",
                refId: typedRef.Value);
        }

        return pdf;
    }
}
