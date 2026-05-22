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

namespace MisaConnect.EInvoice.UnitTests.Amendments;

/// <summary>
/// Slice 6 (FR-066, T017-T020 / T-RP-12) — verifies the
/// <see cref="MeInvoiceCallLogger"/> amendment shape:
/// <c>eInvoiceStatus</c> + <c>orgRefId</c> always present;
/// <c>changeReason</c> flag-gated on <c>Misa:Delete:IncludeRawErrorMessage</c>
/// and passed through <see cref="MeInvoiceLogScrubber.Redact"/>; tokens /
/// buyer info / line items / amounts never logged.
/// </summary>
public class AmendmentLogShapeTests
{
    private static readonly OriginalInvoiceReference Ref = new(
        "orig-001", "00000123", "1", "C26TAA", new DateOnly(2026, 5, 10));

    [Fact]
    public async Task Default_log_omits_ChangeReason_key_entirely()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = MakeDecorator(capture, includeRawErrorMessage: false);

        await decorator.IssueReplacementAsync(
            SampleInvoices.TypicalVatInvoice(),
            Ref,
            "Sửa thông tin",
            invoiceWithCode: true,
            default);

        Assert.NotEmpty(capture.Entries);
        foreach (var (_, msg) in capture.Entries)
        {
            Assert.DoesNotContain("changeReason", msg, StringComparison.OrdinalIgnoreCase);
            // eInvoiceStatus + orgRefId always present
            Assert.Contains("eInvoiceStatus", msg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("orgRefId", msg, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Flag_on_includes_redacted_ChangeReason_in_log()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = MakeDecorator(capture, includeRawErrorMessage: true);

        await decorator.IssueReplacementAsync(
            SampleInvoices.TypicalVatInvoice(),
            Ref,
            "Sửa thông tin",
            invoiceWithCode: true,
            default);

        // At least one entry now carries the changeReason key.
        Assert.Contains(capture.Entries, e =>
            e.Message.Contains("changeReason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Adjustment_log_carries_EInvoiceStatus_4()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = MakeDecorator(capture, includeRawErrorMessage: false);

        await decorator.IssueAdjustmentAsync(
            SampleInvoices.TypicalVatInvoice(),
            Ref,
            "Bổ sung",
            invoiceWithCode: true,
            default);

        Assert.NotEmpty(capture.Entries);
        Assert.Contains(capture.Entries, e =>
            e.Message.Contains("eInvoiceStatus=4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacement_log_carries_EInvoiceStatus_3()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decorator = MakeDecorator(capture, includeRawErrorMessage: false);

        await decorator.IssueReplacementAsync(
            SampleInvoices.TypicalVatInvoice(),
            Ref,
            "Sửa",
            invoiceWithCode: true,
            default);

        Assert.Contains(capture.Entries, e =>
            e.Message.Contains("eInvoiceStatus=3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Never_logs_buyer_or_line_items_or_amounts()
    {
        var capture = new CapturingLogger<MeInvoiceCallLogger>();
        var decoratorOff = MakeDecorator(capture, includeRawErrorMessage: false);
        var invoice = SampleInvoices.TypicalVatInvoice();

        await decoratorOff.IssueReplacementAsync(invoice, Ref, "Sửa", invoiceWithCode: true, default);
        await decoratorOff.IssueAdjustmentAsync(invoice, Ref, "Sửa", invoiceWithCode: true, default);

        var captureOn = new CapturingLogger<MeInvoiceCallLogger>();
        var decoratorOn = MakeDecorator(captureOn, includeRawErrorMessage: true);
        await decoratorOn.IssueReplacementAsync(invoice, Ref, "Sửa", invoiceWithCode: true, default);
        await decoratorOn.IssueAdjustmentAsync(invoice, Ref, "Sửa", invoiceWithCode: true, default);

        foreach (var entries in new[] { capture.Entries, captureOn.Entries })
        {
            foreach (var (_, msg) in entries)
            {
                Assert.DoesNotContain("accountObjectName", msg, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("accountObjectAddress", msg, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("invoiceDetails", msg, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("totalAmount", msg, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static MeInvoiceCallLogger MakeDecorator(
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
            inner: new EchoClient(),
            logger: logger,
            correlation: new StaticCorrelation("cid-am"),
            options: options);
    }

    private sealed class StaticCorrelation : ICorrelationIdAccessor
    {
        public StaticCorrelation(string cid) => CorrelationId = cid;
        public string CorrelationId { get; }
    }

    private sealed class EchoClient : IMeInvoiceClient
    {
        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) =>
            Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<SaveResult> IssueReplacementAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct) =>
            Task.FromResult(new SaveResult(invoice.RefId, SaveOutcome.Success));

        public Task<SaveResult> IssueAdjustmentAsync(
            Invoice invoice,
            OriginalInvoiceReference originalRef,
            string changeReason,
            bool invoiceWithCode,
            CancellationToken ct) =>
            Task.FromResult(new SaveResult(invoice.RefId, SaveOutcome.Success));
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
