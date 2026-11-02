using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.Logging;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Lookup;

/// <summary>
/// FR-053 log-shape tests T17–T21. Verifies the
/// <see cref="MeInvoiceCallLogger"/> decorator emits the right number of
/// log entries per chunk call, summarises large RefID sets, enumerates
/// small ones, and honours the
/// <c>Misa:Delete:IncludeRawErrorMessage</c> flag (reused by slice 5 per
/// R-LU-15).
/// </summary>
public class LookupLogShapeTests
{
    [Fact]
    public async Task One_log_per_chunk_call()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = NewDecorator(new SuccessClient(), capture, includeRawErrorMessage: false);

        // Two chunk calls — the decorator emits one "started" + one "completed"
        // pair per call. FR-053 asserts ONE entry-per-chunk for the completed
        // entries with endpoint=LookupByRefId.
        await decorator.LookupByRefIdAsync(new[] { RefId.From("a") }, true, default);
        await decorator.LookupByRefIdAsync(new[] { RefId.From("b") }, true, default);

        var completed = capture.Entries.Count(e =>
            e.Message.Contains("MeInvoice call completed", StringComparison.Ordinal)
            && e.Message.Contains("LookupByRefId", StringComparison.Ordinal));
        Assert.Equal(2, completed);
    }

    [Fact]
    public async Task RefIdSet_summarised_for_large_chunks()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = NewDecorator(new SuccessClient(), capture, includeRawErrorMessage: false);

        var refIds = new RefId[50];
        for (var i = 0; i < 50; i++) refIds[i] = RefId.From($"r-{i:D4}");

        await decorator.LookupByRefIdAsync(refIds, true, default);

        var startedEntry = capture.Entries.First(e =>
            e.Message.Contains("MeInvoice call started", StringComparison.Ordinal)
            && e.Message.Contains("LookupByRefId", StringComparison.Ordinal));
        // SummariseRefIds composes "{first}+49-more" for > 5 refIds.
        Assert.Contains("r-0000+49-more", startedEntry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefIdSet_enumerated_for_small_chunks()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = NewDecorator(new SuccessClient(), capture, includeRawErrorMessage: false);

        var refIds = new[] { RefId.From("a"), RefId.From("b"), RefId.From("c") };

        await decorator.LookupByRefIdAsync(refIds, true, default);

        var startedEntry = capture.Entries.First(e =>
            e.Message.Contains("MeInvoice call started", StringComparison.Ordinal)
            && e.Message.Contains("LookupByRefId", StringComparison.Ordinal));
        // SummariseRefIds joins each RefId.Value with commas for ≤ 5 refIds.
        Assert.Contains("a,b,c", startedEntry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_strips_raw_ErrorMessage()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = NewDecorator(
            new ThrowingClient("Sensitive raw text from MISA."),
            capture,
            includeRawErrorMessage: false);

        await Assert.ThrowsAsync<MeInvoiceException>(() =>
            decorator.LookupByRefIdAsync(new[] { RefId.From("r1") }, true, default));

        var warning = capture.Entries.Single(e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain("errorMessage", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flag_on_includes_raw_ErrorMessage_after_scrub()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        // Bearer-shaped token inside a JSON "authorization" pair → trips the scrubber.
        var raw = @"{""authorization"":""Bearer abc123""} contextual text";
        var decorator = NewDecorator(
            new ThrowingClient(raw),
            capture,
            includeRawErrorMessage: true);

        await Assert.ThrowsAsync<MeInvoiceException>(() =>
            decorator.LookupByRefIdAsync(new[] { RefId.From("r1") }, true, default));

        var warning = capture.Entries.Single(e => e.Level == LogLevel.Warning);
        Assert.Contains("errorMessage", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer abc123", warning.Message, StringComparison.Ordinal);
        Assert.Contains("contextual text", warning.Message, StringComparison.Ordinal);
    }

    private static MeInvoiceCallLogger NewDecorator(
        IMeInvoiceClient inner,
        ILogger<MeInvoiceCallLogger> logger,
        bool includeRawErrorMessage)
    {
        var options = Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "00",
            UserName = "u",
            Password = "p",
            AppId = "1",
            Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = includeRawErrorMessage },
        });
        return new MeInvoiceCallLogger(
            inner: inner,
            logger: logger,
            correlation: new StaticCorrelation("cid-1"),
            options: options);
    }

    private sealed class StaticCorrelation : ICorrelationIdAccessor
    {
        public StaticCorrelation(string cid) => CorrelationId = cid;
        public string CorrelationId { get; }
    }

    private sealed class SuccessClient : IMeInvoiceClient
    {
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
            => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
            IReadOnlyList<RefId> refIds,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            // Return successful empty chunk; the test focuses on log shape, not
            // outcomes, so an empty snapshot list is sufficient and avoids
            // building 50 snapshot records just to log them.
            return Task.FromResult(new LookupByRefIdChunkResult(
                Array.Empty<InvoiceSnapshot>(),
                ErrorCode: null,
                ErrorMessage: null));
        }
    }

    private sealed class ThrowingClient : IMeInvoiceClient
    {
        private readonly string _message;
        public ThrowingClient(string message) => _message = message;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
            => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();

        public Task<LookupByRefIdChunkResult> LookupByRefIdAsync(
            IReadOnlyList<RefId> refIds,
            bool invoiceWithCode,
            CancellationToken ct)
        {
            throw new MeInvoiceException(MeInvoiceErrorCategory.MisaUnknown, "X", _message);
        }
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
