using MisaConnect.EInvoice.Domain.Invoices;

namespace MisaConnect.EInvoice.Application.Abstractions;

public interface IRefIdGenerator
{
    RefId NewRefId();
}

public sealed class GuidRefIdGenerator : IRefIdGenerator
{
    public RefId NewRefId() => RefId.NewGuid();
}
