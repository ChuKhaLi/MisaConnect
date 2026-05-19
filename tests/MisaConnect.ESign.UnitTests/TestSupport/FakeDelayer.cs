using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.UnitTests.TestSupport;

internal sealed class FakeDelayer : IDelayer
{
    public int Calls { get; private set; }
    public List<TimeSpan> Delays { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        Calls++;
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock : ISystemClock
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start)
    {
        _now = start;
    }

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

internal sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    public StubCorrelationIdAccessor(string value = "cid-test")
    {
        Current = value;
    }

    public string Current { get; }
}
