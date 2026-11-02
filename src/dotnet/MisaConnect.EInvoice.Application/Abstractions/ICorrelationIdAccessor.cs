namespace MisaConnect.EInvoice.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
