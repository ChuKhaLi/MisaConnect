using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Http;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class RefreshTokenSingleFlightTests
{
    [Fact]
    public async Task Eight_parallel_callers_fire_one_factory_invocation()
    {
        var slot = new SingleFlightRefresh();
        var invocations = 0;
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, Task<AccessToken>> factory = ct =>
        {
            Interlocked.Increment(ref invocations);
            return gate.Task;
        };

        var pending = new List<Task<AccessToken>>(8);
        for (var i = 0; i < 8; i++)
        {
            pending.Add(slot.RefreshAsync("cache-key", factory, CancellationToken.None));
        }

        gate.SetResult(new AccessToken("rs", "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "user"));
        var results = await Task.WhenAll(pending);

        Assert.Equal(1, invocations);
        Assert.All(results, r => Assert.Equal("rs", r.Value));
    }

    [Fact]
    public async Task After_completion_next_wave_starts_a_fresh_factory()
    {
        var slot = new SingleFlightRefresh();
        var invocations = 0;
        Func<CancellationToken, Task<AccessToken>> factory = ct =>
        {
            invocations++;
            return Task.FromResult(new AccessToken("rs", "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "user"));
        };

        await slot.RefreshAsync("k", factory, CancellationToken.None);
        await Task.Delay(50);
        await slot.RefreshAsync("k", factory, CancellationToken.None);

        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task Failing_factory_propagates_to_all_waiters()
    {
        var slot = new SingleFlightRefresh();
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<AccessToken>> factory = ct => gate.Task;

        var pending = new List<Task<AccessToken>>(8);
        for (var i = 0; i < 8; i++)
        {
            pending.Add(slot.RefreshAsync("k2", factory, CancellationToken.None));
        }

        gate.SetException(new AuthenticationFailedException("rejected", "refresh failed", "cid"));

        foreach (var t in pending)
        {
            await Assert.ThrowsAsync<AuthenticationFailedException>(() => t);
        }
    }
}
