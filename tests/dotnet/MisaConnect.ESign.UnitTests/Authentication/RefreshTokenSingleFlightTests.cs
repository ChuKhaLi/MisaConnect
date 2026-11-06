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
    public async Task First_caller_cancelling_does_not_cancel_the_flight_for_other_waiters()
    {
        var slot = new SingleFlightRefresh();
        var invocations = 0;
        CancellationToken factoryToken = default;
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<AccessToken>> factory = ct =>
        {
            Interlocked.Increment(ref invocations);
            factoryToken = ct;
            return gate.Task;
        };

        using var cts = new CancellationTokenSource();
        var a = slot.RefreshAsync("k3", factory, cts.Token);
        var b = slot.RefreshAsync("k3", factory, CancellationToken.None);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(b.IsCompleted);

        gate.SetResult(new AccessToken("rs", "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "user"));
        var result = await b;

        Assert.Equal("rs", result.Value);
        Assert.Equal(1, invocations);
        Assert.False(factoryToken.CanBeCanceled);
    }

    [Fact]
    public async Task Flight_completes_even_when_every_waiter_cancels()
    {
        var slot = new SingleFlightRefresh();
        var invocations = 0;
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AccessToken>? firstFlight = null;
        Func<CancellationToken, Task<AccessToken>> factory = ct =>
        {
            if (Interlocked.Increment(ref invocations) == 1)
            {
                firstFlight = gate.Task;
                return gate.Task;
            }
            return Task.FromResult(new AccessToken("rs2", "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "user"));
        };

        using var cts = new CancellationTokenSource();
        var a = slot.RefreshAsync("k4", factory, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(TimeSpan.FromSeconds(5)));

        // The flight is still in the slot until it completes: a new caller joins it.
        var joined = slot.RefreshAsync("k4", factory, CancellationToken.None);
        Assert.Equal(1, invocations);

        gate.SetResult(new AccessToken("rs", "raw", "rt", DateTimeOffset.UtcNow.AddMinutes(60), "u", "user"));
        Assert.Equal("rs", (await joined).Value);
        Assert.True(firstFlight!.IsCompletedSuccessfully);

        await Task.Delay(50);
        var next = await slot.RefreshAsync("k4", factory, CancellationToken.None);
        Assert.Equal("rs2", next.Value);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task Faulted_flight_with_no_waiters_left_is_observed()
    {
        var marker = "flight-fault-" + Guid.NewGuid().ToString("N");
        var unobserved = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, e) =>
        {
            if (e.Exception.Flatten().InnerExceptions.Any(x => x.Message == marker))
            {
                Interlocked.Increment(ref unobserved);
            }
        };
        TaskScheduler.UnobservedTaskException += onUnobserved;
        try
        {
            var slot = new SingleFlightRefresh();
            await RunAndAbandonAsync(slot, marker);

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobserved;
        }
    }

    // Kept out of line so no local keeps the faulted flight task reachable.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task RunAndAbandonAsync(SingleFlightRefresh slot, string marker)
    {
        var gate = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var waiter = slot.RefreshAsync("k-fault", _ => gate.Task, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
        gate.SetException(new InvalidOperationException(marker));
        await Task.Delay(50);
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
