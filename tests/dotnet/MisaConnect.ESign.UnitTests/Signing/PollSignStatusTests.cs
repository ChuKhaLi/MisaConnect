using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing;

public class PollSignStatusTests
{
    [Fact]
    public async Task Returns_immediately_on_success()
    {
        var wire = new StubWireClient
        {
            OnGetStatus = (_, tx, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.SUCCESS, null, null, tx, "sig-bytes")),
        };
        var poll = new PollSignStatus(wire, new FakeDelayer(), new FakeClock(DateTimeOffset.UtcNow), new StubCorrelationIdAccessor());

        var snapshot = await poll.ExecuteAsync("token", "tx-1", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.Equal(SignStatus.SUCCESS, snapshot.Status);
        Assert.Equal("sig-bytes", snapshot.FirstSignatureData);
        Assert.Equal(1, wire.GetStatusCalls);
    }

    [Fact]
    public async Task Pending_then_success_after_two_iterations()
    {
        var sequence = new Queue<SignStatusSnapshot>(new[]
        {
            new SignStatusSnapshot(SignStatus.PENDING, null, null, "tx-1", null),
            new SignStatusSnapshot(SignStatus.PENDING, null, null, "tx-1", null),
            new SignStatusSnapshot(SignStatus.SUCCESS, null, null, "tx-1", "sig"),
        });
        var wire = new StubWireClient
        {
            OnGetStatus = (_, _, _) => Task.FromResult(sequence.Dequeue()),
        };
        var delayer = new FakeDelayer();
        var poll = new PollSignStatus(wire, delayer, new FakeClock(DateTimeOffset.UtcNow), new StubCorrelationIdAccessor());

        var snapshot = await poll.ExecuteAsync("token", "tx-1", TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.Equal(SignStatus.SUCCESS, snapshot.Status);
        Assert.Equal(3, wire.GetStatusCalls);
        Assert.Equal(2, delayer.Calls);
    }

    [Fact]
    public async Task Failed_terminal_throws_typed_exception()
    {
        var wire = new StubWireClient
        {
            OnGetStatus = (_, tx, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.FAILED, "InvalidCert", "Bad cert", tx, null)),
        };
        var poll = new PollSignStatus(wire, new FakeDelayer(), new FakeClock(DateTimeOffset.UtcNow), new StubCorrelationIdAccessor());

        var ex = await Assert.ThrowsAsync<SignTerminalStateException>(() =>
            poll.ExecuteAsync("token", "tx-1", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(60), CancellationToken.None));
        Assert.Equal(SignStatus.FAILED, ex.TerminalStatus);
        Assert.Equal("tx-1", ex.TransactionId);
        Assert.Equal("InvalidCert", ex.RawCode);
    }

    [Fact]
    public async Task Cancelled_terminal_throws_typed_exception()
    {
        var wire = new StubWireClient
        {
            OnGetStatus = (_, tx, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.CANCELLED, null, "User cancelled", tx, null)),
        };
        var poll = new PollSignStatus(wire, new FakeDelayer(), new FakeClock(DateTimeOffset.UtcNow), new StubCorrelationIdAccessor());

        var ex = await Assert.ThrowsAsync<SignTerminalStateException>(() =>
            poll.ExecuteAsync("token", "tx-1", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(60), CancellationToken.None));
        Assert.Equal(SignStatus.CANCELLED, ex.TerminalStatus);
    }

    [Fact]
    public async Task Deadline_elapsed_throws_timeout()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var wire = new StubWireClient
        {
            OnGetStatus = (_, _, _) =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(600));
                return Task.FromResult(new SignStatusSnapshot(SignStatus.PENDING, null, null, "tx-1", null));
            },
        };
        var poll = new PollSignStatus(wire, new FakeDelayer(), clock, new StubCorrelationIdAccessor());

        var ex = await Assert.ThrowsAsync<SignTimeoutException>(() =>
            poll.ExecuteAsync("token", "tx-1", TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500), CancellationToken.None));
        Assert.Equal("tx-1", ex.TransactionId);
        Assert.True(ex.ElapsedTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task Unknown_status_throws_terminal_unknown()
    {
        var wire = new StubWireClient
        {
            OnGetStatus = (_, tx, _) => Task.FromResult(new SignStatusSnapshot(SignStatus.UNKNOWN, "Weird", null, tx, null)),
        };
        var poll = new PollSignStatus(wire, new FakeDelayer(), new FakeClock(DateTimeOffset.UtcNow), new StubCorrelationIdAccessor());

        var ex = await Assert.ThrowsAsync<SignTerminalStateException>(() =>
            poll.ExecuteAsync("token", "tx-1", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(60), CancellationToken.None));
        Assert.Equal(SignStatus.UNKNOWN, ex.TerminalStatus);
    }
}
