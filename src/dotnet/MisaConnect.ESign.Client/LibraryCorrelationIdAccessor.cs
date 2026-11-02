using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Client;

internal sealed class LibraryCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string Current { get; } = Guid.NewGuid().ToString("D");
}
