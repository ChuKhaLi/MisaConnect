using MisaConnect.EInvoice.Application.UseCases;

namespace MisaConnect.EInvoice.Application.Operations;

public sealed record MeInvoiceOperation(
    string Name,
    Type UseCaseType,
    string HttpRoute,
    string ClientMethodName);

public static class MeInvoiceOperations
{
    public static IReadOnlyList<MeInvoiceOperation> All { get; } = new[]
    {
        new MeInvoiceOperation(
            "list-templates",
            typeof(ListActiveTemplates),
            "GET /api/templates",
            "ListTemplatesAsync"),
        new MeInvoiceOperation(
            "preview-invoice",
            typeof(PreviewInvoice),
            "POST /api/invoices/preview",
            "PreviewAsync"),
        new MeInvoiceOperation(
            "save-draft-invoices",
            typeof(SaveDraftInvoices),
            "POST /api/invoices/draft",
            "SaveDraftAsync"),
        new MeInvoiceOperation(
            "get-draft-pdf-by-refid",
            typeof(GetDraftPdfByRefId),
            "GET /api/invoices/{refId}/pdf",
            "GetDraftPdfAsync"),
        new MeInvoiceOperation(
            "delete-draft-invoice",
            typeof(DeleteDraftInvoice),
            "DELETE /api/invoices/{refId}",
            "DeleteDraftAsync"),
        new MeInvoiceOperation(
            "lookup-by-refid",
            typeof(LookupByRefIds),
            "POST /api/invoices/lookup/by-refid",
            "LookupByRefIdAsync"),
        new MeInvoiceOperation(
            "lookup-paged-standard",
            typeof(LookupStandard),
            "POST /api/invoices/lookup/standard",
            "LookupStandardAsync"),
        new MeInvoiceOperation(
            "lookup-paged-calculating",
            typeof(LookupCalculating),
            "POST /api/invoices/lookup/calculating",
            "LookupCalculatingAsync"),
        new MeInvoiceOperation(
            "issue-replacement-invoice",
            typeof(IssueReplacementInvoice),
            "POST /api/invoices/replacement",
            "IssueReplacementAsync"),
        new MeInvoiceOperation(
            "issue-adjustment-invoice",
            typeof(IssueAdjustmentInvoice),
            "POST /api/invoices/adjustment",
            "IssueAdjustmentAsync"),
    };
}
