using System.Net;
using System.Net.Sockets;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Retry;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

/// <summary>
/// Slice 2 T060 — assert <see cref="ThrottleRetryHandler"/> splits
/// transport-layer failures out of <see cref="MeInvoiceErrorCategory.MisaUnavailable"/>
/// into the new <see cref="MeInvoiceErrorCategory.TransportFailed"/> category
/// (research R-DEL-04). 5xx remains <c>MisaUnavailable</c>; 429 remains
/// <c>MisaThrottled</c>; HTTP-no-response and operation-timeout surface as
/// <c>TransportFailed</c>; caller-cancellation propagates unchanged.
/// </summary>
public class ThrottleRetryHandlerTransportTests
{
    [Fact]
    public async Task HttpRequestException_with_no_inner_response_maps_to_TransportFailed()
    {
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new TransportFailingHandler(new HttpRequestException("DNS lookup failed")),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p")));
        Assert.Equal(MeInvoiceErrorCategory.TransportFailed, ex.Category);
    }

    [Fact]
    public async Task TaskCanceledException_due_to_operation_timeout_maps_to_TransportFailed()
    {
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new TransportFailingHandler(new TaskCanceledException("Operation timed out")),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p")));
        Assert.Equal(MeInvoiceErrorCategory.TransportFailed, ex.Category);
    }

    [Fact]
    public async Task HttpRequestException_with_SocketException_inner_maps_to_TransportFailed()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new TransportFailingHandler(new HttpRequestException("Connection refused", inner)),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p")));
        Assert.Equal(MeInvoiceErrorCategory.TransportFailed, ex.Category);
    }

    [Fact]
    public async Task HTTP_5xx_remains_MisaUnavailable_after_retry()
    {
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new FixedStatusHandler(HttpStatusCode.InternalServerError),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p")));
        Assert.Equal(MeInvoiceErrorCategory.MisaUnavailable, ex.Category);
    }

    [Fact]
    public async Task HTTP_429_remains_MisaThrottled_after_retry()
    {
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new FixedStatusHandler(HttpStatusCode.TooManyRequests),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        var ex = await Assert.ThrowsAsync<MeInvoiceException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p")));
        Assert.Equal(MeInvoiceErrorCategory.MisaThrottled, ex.Category);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_unchanged()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ThrottleRetryHandler(_ => Task.CompletedTask)
        {
            InnerHandler = new TransportFailingHandler(new TaskCanceledException()),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/p"), cts.Token));
    }

    private sealed class TransportFailingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public TransportFailingHandler(Exception ex) => _exception = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<HttpResponseMessage>(cancellationToken)
                : throw _exception;
    }

    private sealed class FixedStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        public FixedStatusHandler(HttpStatusCode code) => _code = code;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_code));
    }
}
