using EInvoiceAccessor = MisaConnect.EInvoice.Application.Abstractions.ICorrelationIdAccessor;
using ESignAccessor = MisaConnect.ESign.Application.Abstractions.ICorrelationIdAccessor;

namespace MisaConnect.Samples.Api.Middleware;

public sealed class HttpContextCorrelationIdAccessor : EInvoiceAccessor, ESignAccessor
{
    private string _id = string.Empty;

    public string CorrelationId => _id;

    public string Current => _id;

    internal void SetId(string id) => _id = id ?? string.Empty;
}
