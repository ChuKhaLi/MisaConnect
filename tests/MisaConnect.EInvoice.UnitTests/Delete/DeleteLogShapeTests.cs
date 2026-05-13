using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Slice 2 T061 + T062 (tests T16 + T17) — verify
/// <see cref="MeInvoiceCallLogger"/> honours the
/// <c>Misa:Delete:IncludeRawErrorMessage</c> flag:
/// - Default (flag false): the <c>errorMessage</c> key is ABSENT from the
///   structured log payload — not serialised as null. Achieved by emitting
///   a different message template (without the placeholder) when the flag
///   is off.
/// - Flag true: the <c>errorMessage</c> key is present after
///   <see cref="MeInvoiceLogScrubber.Redact"/> strips known secret tokens.
/// </summary>
public class DeleteLogShapeTests
{
    [Fact]
    public async Task Default_strips_raw_ErrorMessage()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var options = Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "00",
            UserName = "u",
            Password = "p",
            AppId = "1",
            Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = false },
        });

        var decorator = new MeInvoiceCallLogger(
            inner: new ThrowingClient("Sensitive raw text from MISA."),
            logger: capture,
            correlation: new StaticCorrelation("cid-1"),
            options: options);

        await Assert.ThrowsAsync<MeInvoiceException>(() =>
            decorator.DeleteDraftAsync(RefId.From("r1"), true, default));

        var warning = capture.Entries.Single(e => e.Level == LogLevel.Warning);
        // Key omission: with flag off the template does NOT mention "errorMessage".
        Assert.DoesNotContain("errorMessage", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flag_on_includes_raw_ErrorMessage_after_scrub()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var options = Options.Create(new MisaEInvoiceOptions
        {
            Environment = MeInvoiceEnvironment.Sandbox,
            BaseUrl = "https://testapi.meinvoice.vn/api/integration",
            TaxCode = "00",
            UserName = "u",
            Password = "p",
            AppId = "1",
            Delete = new MisaEInvoiceDeleteOptions { IncludeRawErrorMessage = true },
        });

        // The raw text contains a Bearer-shaped substring inside JSON to trigger the
        // scrubber's "authorization" pattern.
        var raw = @"{""authorization"":""Bearer abc123""} contextual text";
        var decorator = new MeInvoiceCallLogger(
            inner: new ThrowingClient(raw),
            logger: capture,
            correlation: new StaticCorrelation("cid-2"),
            options: options);

        await Assert.ThrowsAsync<MeInvoiceException>(() =>
            decorator.DeleteDraftAsync(RefId.From("r1"), true, default));

        var warning = capture.Entries.Single(e => e.Level == LogLevel.Warning);
        Assert.Contains("errorMessage", warning.Message, StringComparison.OrdinalIgnoreCase);
        // The scrubber should have masked "Bearer abc123" inside the JSON token.
        Assert.DoesNotContain("Bearer abc123", warning.Message, StringComparison.Ordinal);
        Assert.Contains("contextual text", warning.Message, StringComparison.Ordinal);
    }

    private sealed class StaticCorrelation : ICorrelationIdAccessor
    {
        public StaticCorrelation(string cid) => CorrelationId = cid;
        public string CorrelationId { get; }
    }

    private sealed class ThrowingClient : IMeInvoiceClient
    {
        private readonly string _message;
        public ThrowingClient(string message) => _message = message;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            throw new MeInvoiceException(MeInvoiceErrorCategory.NotDeletable, "InvalidTransactionID", _message, refId: refId.Value);
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
