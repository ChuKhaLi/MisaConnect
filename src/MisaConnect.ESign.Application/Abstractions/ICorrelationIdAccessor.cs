namespace MisaConnect.ESign.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string Current { get; }
}
