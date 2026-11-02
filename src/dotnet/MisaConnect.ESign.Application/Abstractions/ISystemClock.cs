namespace MisaConnect.ESign.Application.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
