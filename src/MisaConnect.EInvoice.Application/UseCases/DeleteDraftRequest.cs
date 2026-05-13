using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.UseCases;

public sealed record DeleteDraftRequest(RefId RefId, bool InvoiceWithCode);
