using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Time;

public sealed class SystemClock : ISystemClock
{
    private readonly TimeProvider _tp;

    public SystemClock() : this(TimeProvider.System)
    {
    }

    public SystemClock(TimeProvider tp)
    {
        _tp = tp;
    }

    public DateTimeOffset UtcNow => _tp.GetUtcNow();
}
