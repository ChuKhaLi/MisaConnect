using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.Logging;
using MisaConnect.EInvoice.TestSupport.Fixtures;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Logging;

/// <summary>
/// Slice 009 US2 (FR-003, FR-004). The configured account tax code must never appear in a
/// <see cref="MeInvoiceCallLogger"/> entry, neither in the rendered message nor as a structured
/// property, on any outcome and on both decorator paths (plain call and amendment call).
/// </summary>
public class CallLoggerTaxCodeTests
{
    private const string TaxCode = "0109876543";

    private static readonly OriginalInvoiceReference Ref = new(
        "orig-001", "00000123", "1", "C26TAA", new DateOnly(2026, 5, 10));

    public static TheoryData<Outcome, bool> Cases()
    {
        var data = new TheoryData<Outcome, bool>();
        foreach (var outcome in Enum.GetValues<Outcome>())
        {
            data.Add(outcome, false);
            data.Add(outcome, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Plain_call_never_logs_tax_code(Outcome outcome, bool includeRaw)
    {
        var capture = new StructuredCapturingLogger();
        var decorator = MakeDecorator(capture, outcome, includeRaw);

        await Run(() => decorator.ListTemplatesAsync(invoiceWithCode: true, default), outcome);

        AssertNoTaxCode(capture);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Amendment_call_never_logs_tax_code(Outcome outcome, bool includeRaw)
    {
        var capture = new StructuredCapturingLogger();
        var decorator = MakeDecorator(capture, outcome, includeRaw);

        await Run(
            () => decorator.IssueReplacementAsync(SampleInvoices.TypicalVatInvoice(), Ref, "Sửa", invoiceWithCode: true, default),
            outcome);

        AssertNoTaxCode(capture);
    }

    [Fact]
    public async Task Other_fields_are_still_logged()
    {
        var capture = new StructuredCapturingLogger();
        var decorator = MakeDecorator(capture, Outcome.Success, includeRaw: false);

        await decorator.ListTemplatesAsync(invoiceWithCode: true, default);

        var keys = capture.Entries.SelectMany(e => e.State.Select(kv => kv.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "Endpoint", "CorrelationId", "Outcome", "DurationMs", "TemplateId", "RefId" })
        {
            Assert.Contains(expected, keys);
        }
    }

    private static async Task Run<T>(Func<Task<T>> call, Outcome outcome)
    {
        if (outcome == Outcome.Success)
        {
            await call();
            return;
        }
        await Assert.ThrowsAnyAsync<Exception>(call);
    }

    private static void AssertNoTaxCode(StructuredCapturingLogger capture)
    {
        Assert.NotEmpty(capture.Entries);
        foreach (var entry in capture.Entries)
        {
            Assert.DoesNotContain(TaxCode, entry.Message, StringComparison.Ordinal);
            foreach (var (key, value) in entry.State)
            {
                Assert.NotEqual("TaxCode", key);
                Assert.NotEqual(TaxCode, value?.ToString());
            }
        }
    }

    private static MeInvoiceCallLogger MakeDecorator(ILogger<MeInvoiceCallLogger> logger, Outcome outcome, bool includeRaw)
    {
        var options = Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = TaxCode,
            UserName = "u",
            Password = "p",
            AppId = "1",
            Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = includeRaw },
        });
        return new MeInvoiceCallLogger(new StubClient(outcome), logger, new StaticCorrelation("cid-tax"), options);
    }

    public enum Outcome
    {
        Success,
        MisaError,
        UnexpectedError,
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly Outcome _outcome;
        public StubClient(Outcome outcome) => _outcome = outcome;

        private Task<T> Respond<T>(T value) => _outcome switch
        {
            Outcome.Success => Task.FromResult(value),
            // The raw MISA text is surfaced as-is when opted in (Principle VIII); this test only
            // proves the logger itself never adds the configured tax code.
            Outcome.MisaError => throw new MeInvoiceException(
                MeInvoiceErrorCategory.Validation, "InvalidInvoiceData", "invoice data invalid"),
            _ => throw new InvalidOperationException("boom"),
        };

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
            Respond<IReadOnlyList<Template>>(Array.Empty<Template>());
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<SaveResult> IssueReplacementAsync(
            Invoice invoice, OriginalInvoiceReference originalRef, string changeReason, bool invoiceWithCode, CancellationToken ct) =>
            Respond(new SaveResult(invoice.RefId, SaveOutcome.Success));
        public Task<SaveResult> IssueAdjustmentAsync(
            Invoice invoice, OriginalInvoiceReference originalRef, string changeReason, bool invoiceWithCode, CancellationToken ct) =>
            Respond(new SaveResult(invoice.RefId, SaveOutcome.Success));
    }

    private sealed class StaticCorrelation : ICorrelationIdAccessor
    {
        public StaticCorrelation(string cid) => CorrelationId = cid;
        public string CorrelationId { get; }
    }

    private sealed class StructuredCapturingLogger : ILogger<MeInvoiceCallLogger>
    {
        public List<(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State)> Entries { get; } = new();

        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var kvs = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToList()
                : new List<KeyValuePair<string, object?>>();
            Entries.Add((logLevel, formatter(state, exception), kvs));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
