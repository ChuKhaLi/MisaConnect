namespace MisaConnect.ESign.Application.Abstractions;

public interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}
