using MisaConnect.EInvoice.Application.Abstractions;

namespace MisaConnect.Samples.Api.Middleware;

public sealed class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private string _id = string.Empty;

    public string CorrelationId => _id;

    internal void SetId(string id) => _id = id ?? string.Empty;
}
