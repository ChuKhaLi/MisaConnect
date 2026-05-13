namespace MisaConnect.EInvoice.Application.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISystemClock
{
    private readonly TimeProvider _tp;
    public SystemClock(TimeProvider tp) => _tp = tp;
    public DateTimeOffset UtcNow => _tp.GetUtcNow();
}
