using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Infrastructure.Time;

internal sealed class TaskDelayer : IDelayer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}
