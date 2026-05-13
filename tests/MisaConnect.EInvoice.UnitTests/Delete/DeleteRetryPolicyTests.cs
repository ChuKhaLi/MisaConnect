using System.Net;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.MeInvoice;
using MisaConnect.EInvoice.Infrastructure.MeInvoice.Retry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T058 + T059 — verify the single-bounded-retry contract (FR-036).
/// The retry policy lives in <see cref="ThrottleRetryHandler"/>, so these
/// tests assemble the real handler chain over a counting mock
/// <see cref="HttpMessageHandler"/> and assert call counts. Retry triggers:
/// 429, 5xx, transport failure (no response). NOT retried: terminal
/// envelopes that surface as <see cref="DeleteDraftStatus.NotFound"/>,
/// <c>NotDeletable</c>, <c>AuthFailed</c>, <c>Configuration</c>,
/// <c>MisaUnknown</c>.
/// </summary>
public class DeleteRetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, DeleteDraftStatus.Deleted)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DeleteDraftStatus.Deleted)]
    public async Task Single_retry_on_429_then_5xx_recovers(HttpStatusCode failureCode, DeleteDraftStatus expected)
    {
        var counter = new CountingHandler(new[]
        {
            (failureCode, """{"success":false}"""),
            (HttpStatusCode.OK, """{"success":true}"""),
        });
        var sut = AssembleUseCase(counter, out var disambiguator);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public async Task Single_retry_on_transport_failure_recovers()
    {
        var counter = new CountingHandler(new[]
        {
            (HttpStatusCode.OK, "transport-failure"),    // sentinel triggering transport exception
            (HttpStatusCode.OK, """{"success":true}"""),
        });
        var sut = AssembleUseCase(counter, out _);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.Deleted, outcome.Status);
        Assert.Equal(2, counter.Calls);
    }

    [Theory]
    [InlineData("InvalidTransactionID", "RefID không tồn tại.", DeleteDraftStatus.NotFound)]
    [InlineData("InvalidTransactionID", "Hóa đơn đã phát hành nên không thể xóa.", DeleteDraftStatus.NotDeletable)]
    [InlineData("LicenseInfo_Expired", "License expired.", DeleteDraftStatus.Configuration)]
    [InlineData("WildcardCode", "Weird.", DeleteDraftStatus.MisaUnknown)]
    public async Task No_retry_on_terminal_outcomes(string errorCode, string errorMessage, DeleteDraftStatus expected)
    {
        // Terminal envelopes from MISA arrive with HTTP 200 + envelope.success=false.
        // The retry handler never sees them (it only retries 5xx, 429, transport
        // failures). The use case's outcome mapping decides the status.
        var json = $$"""{"success":false,"errorCode":"{{errorCode}}","ErrorMessage":"{{errorMessage}}"}""";
        var counter = new CountingHandler(new[] { (HttpStatusCode.OK, json) });
        var sut = AssembleUseCase(counter, out _);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(expected, outcome.Status);
        Assert.Equal(1, counter.Calls);
    }

    private static DeleteDraftRequest NewRequest() =>
        new(RefId.From("retry-1"), InvoiceWithCode: true);

    private static DeleteDraftInvoice AssembleUseCase(CountingHandler counter, out InvalidTransactionDisambiguator disambiguator)
    {
        var retry = new ThrottleRetryHandler(_ => Task.CompletedTask) { InnerHandler = counter };
        var http = new HttpClient(retry) { BaseAddress = new Uri("https://testapi.meinvoice.vn/api/integration/") };
        var options = Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "0000000000",
            AppId = "267",
            UserName = "u",
            Password = "p",
        });
        var concrete = new MeInvoiceClient(http, options);
        disambiguator = new InvalidTransactionDisambiguator();

        // Pre-seed the token cache so EnsureAccessToken does NOT call AcquireToken
        // through the same HttpClient pipeline (which would consume our test
        // responses meant for the delete request).
        var cache = new InMemoryTokenCache();
        cache.SetAsync("k", new AccessToken("pre-seeded", DateTimeOffset.UtcNow.AddDays(7)), default).GetAwaiter().GetResult();
        var ensure = new EnsureAccessToken(concrete, cache, new SystemClock(TimeProvider.System), () => "k");

        return new DeleteDraftInvoice(
            concrete,
            ensure,
            disambiguator,
            new StubDeleteOptions(false),
            NullLogger<DeleteDraftInvoice>.Instance);
    }

    private sealed class StubDeleteOptions : IDeleteOptionsAccessor
    {
        public StubDeleteOptions(bool include) => IncludeRawErrorMessage = include;
        public bool IncludeRawErrorMessage { get; }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _queue;
        public int Calls { get; private set; }

        public CountingHandler(IEnumerable<(HttpStatusCode, string)> responses) => _queue = new Queue<(HttpStatusCode, string)>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (_queue.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true}"""),
                });
            }

            var next = _queue.Dequeue();
            if (next.Body == "transport-failure")
            {
                throw new HttpRequestException("simulated transport failure");
            }
            return Task.FromResult(new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Body),
            });
        }
    }
}
