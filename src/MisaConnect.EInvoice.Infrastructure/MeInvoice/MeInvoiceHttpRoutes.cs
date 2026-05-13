namespace MisaConnect.EInvoice.Infrastructure.MeInvoice;

internal static class MeInvoiceHttpRoutes
{
    public const string Token = "webapp/token";
    public const string Templates = "webapp/templates";
    public const string Preview = "webapp/preview";
    public const string Insert = "webapp/insert";
    public const string ViewRefId = "webapp/viewrefid";
    public const string DeleteDraft = "webapp/delete";

    // Slice 5 — read-only lookup endpoints.
    public const string LookupByRefId = "webapp/getlist";
    public const string LookupStandardPaging = "webapp/paging";
    public const string LookupCalculatingPaging = "webapp/paging/calculating";
}
