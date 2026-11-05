using System.Net;
using Microsoft.Extensions.Options;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Configuration;
using MisaConnect.ESign.Infrastructure.Http;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Http;

public class TransientFailureRetryHandlerTests
{
    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<object> _results;

        public QueueHandler(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public int Sends { get; private set; }

        public Action? OnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sends++;
            OnSend?.Invoke();
            var next = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
            return next switch
            {
                Exception ex => Task.FromException<HttpResponseMessage>(ex),
                HttpStatusCode code => Task.FromResult(new HttpResponseMessage(code)),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    private static HttpMessageInvoker Build(QueueHandler inner)
    {
        var handler = new TransientFailureRetryHandler(
            Options.Create(new MisaESignOptions()),
            new FakeDelayer(),
            new StubCorrelationIdAccessor())
        {
            InnerHandler = inner,
        };
        return new HttpMessageInvoker(handler);
    }

    private static HttpRequestMessage Request(bool flagged)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.test/x");
        if (flagged)
        {
            req.Options.Set(ESignRequestOptions.NotRetryableAfterSend, true);
        }
        return req;
    }

    [Fact]
    public async Task Flagged_500_is_sent_once()
    {
        var inner = new QueueHandler(HttpStatusCode.InternalServerError);
        var ex = await Assert.ThrowsAsync<ESignTransportException>(() =>
            Build(inner).SendAsync(Request(true), CancellationToken.None));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.LastStatusCode);
        Assert.Equal(1, ex.AttemptCount);
        Assert.Equal(1, inner.Sends);
    }

    [Fact]
    public async Task Flagged_timeout_is_sent_once()
    {
        var inner = new QueueHandler(new TaskCanceledException("timeout"));
        await Assert.ThrowsAsync<ESignTransportException>(() =>
            Build(inner).SendAsync(Request(true), CancellationToken.None));
        Assert.Equal(1, inner.Sends);
    }

    [Fact]
    public async Task Flagged_response_ended_is_sent_once()
    {
        var inner = new QueueHandler(new HttpRequestException(HttpRequestError.ResponseEnded, "x"));
        await Assert.ThrowsAsync<ESignTransportException>(() =>
            Build(inner).SendAsync(Request(true), CancellationToken.None));
        Assert.Equal(1, inner.Sends);
    }

    [Fact]
    public async Task Flagged_429_is_retried()
    {
        var inner = new QueueHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        using var resp = await Build(inner).SendAsync(Request(true), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, inner.Sends);
    }

    [Fact]
    public async Task Flagged_connection_refused_is_retried()
    {
        var inner = new QueueHandler(new HttpRequestException(HttpRequestError.ConnectionError, "x"), HttpStatusCode.OK);
        using var resp = await Build(inner).SendAsync(Request(true), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, inner.Sends);
    }

    [Fact]
    public async Task Flagged_dns_failure_is_retried()
    {
        var inner = new QueueHandler(new HttpRequestException(HttpRequestError.NameResolutionError, "x"), HttpStatusCode.OK);
        using var resp = await Build(inner).SendAsync(Request(true), CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(2, inner.Sends);
    }

    [Fact]
    public async Task Unflagged_500_is_retried_to_MaxAttempts()
    {
        var inner = new QueueHandler(HttpStatusCode.InternalServerError);
        var ex = await Assert.ThrowsAsync<ESignTransportException>(() =>
            Build(inner).SendAsync(Request(false), CancellationToken.None));
        Assert.Equal(3, inner.Sends);
        Assert.Equal(3, ex.AttemptCount);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var inner = new QueueHandler(new TaskCanceledException("cancelled", null, cts.Token));
        inner.OnSend = () => cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            Build(inner).SendAsync(Request(true), cts.Token));
        Assert.Equal(1, inner.Sends);
    }
}
