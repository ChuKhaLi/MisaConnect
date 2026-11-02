using MisaConnect.EInvoice.Application.Abstractions;

namespace MisaConnect.EInvoice.Client;

internal sealed class LibraryCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string CorrelationId { get; } = Guid.NewGuid().ToString("D");
}
