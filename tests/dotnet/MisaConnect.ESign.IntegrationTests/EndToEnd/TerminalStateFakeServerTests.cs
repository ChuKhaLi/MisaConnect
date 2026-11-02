using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class TerminalStateFakeServerTests
{
    [Fact]
    public async Task Failed_status_surfaces_typed_exception()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysFailed = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<SignTerminalStateException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Equal(SignStatus.FAILED, ex.TerminalStatus);
        Assert.Equal("tx-fake-1", ex.TransactionId);
    }

    [Fact]
    public async Task Cancelled_status_surfaces_typed_exception()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysCancelled = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<SignTerminalStateException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Equal(SignStatus.CANCELLED, ex.TerminalStatus);
    }

    [Fact]
    public async Task Always_pending_surfaces_timeout()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.StatusAlwaysPending = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl, o =>
        {
            o.Polling.Interval = TimeSpan.FromMilliseconds(10);
            o.Polling.TotalTimeout = TimeSpan.FromMilliseconds(100);
        });

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<SignTimeoutException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Equal("tx-fake-1", ex.TransactionId);
    }
}
