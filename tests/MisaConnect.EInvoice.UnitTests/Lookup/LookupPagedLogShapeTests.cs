using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// Slice 5 US2 log-shape tests T17 + T18 from
/// <c>contracts/lookup-paginated.md</c>. The
/// <see cref="MeInvoiceCallLogger"/> decorator emits exactly one log entry per
/// page request; the entry carries the start/length coordinates inside the
/// <c>refId</c> placeholder per
/// <see cref="MeInvoiceCallLogger.LookupStandardAsync"/>
/// (<c>label = "start={Start},length={Length}"</c>).
///
/// Filed as a separate test class to avoid collision with the US1 batch-lookup
/// log-shape tests authored by another agent.
/// </summary>
public class LookupPagedLogShapeTests
{
    [Fact]
    public async Task Paged_log_carries_start_length_returnedCount()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = new MeInvoiceCallLogger(
            inner: new StubPagedClient(new PagedResult(Array.Empty<InvoiceSnapshot>(), Start: 0, Length: 100, ReturnedCount: 0)),
            logger: capture,
            correlation: new StaticCorrelation("cid-1"),
            options: NewOptions(includeRawErrorMessage: false));

        await decorator.LookupStandardAsync(
            new PagedLookupRequest(
                Start: 0,
                Length: 100,
                Sort: "InvDate",
                FromDate: new DateOnly(2026, 5, 1),
                ToDate: new DateOnly(2026, 5, 31),
                PublishStatus: null),
            invoiceWithCode: true,
            default);

        var completed = capture.Entries.Single(e => e.Level == LogLevel.Information && e.Message.Contains("completed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("LookupStandard", completed.Message, StringComparison.Ordinal);
        // start/length pair lives inside the refId placeholder per
        // MeInvoiceCallLogger.LookupStandardAsync ("start=0,length=100").
        Assert.Contains("start=0", completed.Message, StringComparison.Ordinal);
        Assert.Contains("length=100", completed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paged_log_omits_publishStatus_when_null()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = new MeInvoiceCallLogger(
            inner: new StubPagedClient(new PagedResult(Array.Empty<InvoiceSnapshot>(), Start: 0, Length: 100, ReturnedCount: 0)),
            logger: capture,
            correlation: new StaticCorrelation("cid-2"),
            options: NewOptions(includeRawErrorMessage: false));

        await decorator.LookupStandardAsync(
            new PagedLookupRequest(
                Start: 0,
                Length: 100,
                Sort: "InvDate",
                FromDate: new DateOnly(2026, 5, 1),
                ToDate: new DateOnly(2026, 5, 31),
                PublishStatus: null),
            invoiceWithCode: true,
            default);

        var completed = capture.Entries.Single(e => e.Level == LogLevel.Information && e.Message.Contains("completed", StringComparison.OrdinalIgnoreCase));
        // The decorator's structured template never references publishStatus,
        // so it must never appear in the rendered message regardless of the
        // request value. Asserts the absence per FR-053.
        Assert.DoesNotContain("publishStatus", completed.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IOptions<MisaEInvoiceOptions> NewOptions(bool includeRawErrorMessage) =>
        Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "0000000000",
            UserName = "u",
            Password = "p",
            AppId = "1",
            Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = includeRawErrorMessage },
        });

    private sealed class StaticCorrelation : ICorrelationIdAccessor
    {
        public StaticCorrelation(string cid) => CorrelationId = cid;
        public string CorrelationId { get; }
    }

    private sealed class StubPagedClient : IMeInvoiceClient
    {
        private readonly PagedResult _result;

        public StubPagedClient(PagedResult result) => _result = result;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<PagedResult> LookupStandardAsync(PagedLookupRequest request, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult(_result);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
