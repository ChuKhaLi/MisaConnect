namespace MisaConnect.EInvoice.Infrastructure.Configuration;

public sealed class MisaEInvoiceOptions
{
    public const string SectionName = "Misa:EInvoice";

    public const string SandboxHost = "testapi.meinvoice.vn";
    public const string ProductionHost = "api.meinvoice.vn";

    public MeInvoiceEnvironment Environment { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string TaxCode { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public MisaEInvoiceDeleteOptions Delete { get; set; } = new();
}

public sealed class MisaEInvoiceDeleteOptions
{
    public bool IncludeRawErrorMessage { get; set; }
}
