using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Errors;
using MisaConnect.EInvoice.Application.Results;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Delete;

/// <summary>
/// Spec contract tests T1–T11 for <see cref="DeleteDraftInvoice"/>. Each test
/// drives the use case with a configured <see cref="StubClient"/> and asserts
/// the resulting <see cref="DeleteDraftOutcome"/> matches the FR-034 row in
/// the disambiguation table.
/// </summary>
public class DeleteDraftInvoiceTests
{
    [Fact]
    public async Task Deleted_on_success_envelope()
    {
        var stub = new StubClient(new DeleteResponse(true, null, null));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.Deleted, outcome.Status);
        Assert.Null(outcome.RawErrorCode);
        Assert.Null(outcome.Message);
        Assert.Null(outcome.Field);
    }

    [Fact]
    public async Task NotDeletable_on_InvalidTransactionID_plus_phat_hanh()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "Hóa đơn đã phát hành nên không thể xóa."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotDeletable, outcome.Status);
        Assert.Equal("InvalidTransactionID", outcome.RawErrorCode);
        Assert.Null(outcome.Message);
    }

    [Fact]
    public async Task NotFound_on_InvalidTransactionID_plus_khong_ton_tai()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "RefID không tồn tại."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotFound, outcome.Status);
        Assert.Equal("InvalidTransactionID", outcome.RawErrorCode);
    }

    [Fact]
    public async Task NotFound_on_empty_ErrorMessage_emits_drift_warning()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", ""));
        var logger = new CapturingLogger();
        var sut = MakeSut(stub, includeMessage: false, logger: logger);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotFound, outcome.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ARCH-DEL-001"));
    }

    [Fact]
    public async Task NotFound_on_unmatched_ErrorMessage_emits_drift_warning()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "Hóa đơn đang trong trạng thái khóa."));
        var logger = new CapturingLogger();
        var sut = MakeSut(stub, includeMessage: false, logger: logger);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.NotFound, outcome.Status);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("ARCH-DEL-001"));
    }

    [Fact]
    public async Task AuthFailed_after_refresh_retry_exhausted()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.Authentication, "UnAuthorize"));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.AuthFailed, outcome.Status);
        Assert.Equal("UnAuthorize", outcome.RawErrorCode);
    }

    [Fact]
    public async Task Configuration_for_LicenseInfo_Expired()
    {
        var stub = new StubClient(new DeleteResponse(false, "LicenseInfo_Expired", "License expired."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.Configuration, outcome.Status);
        Assert.Equal("LicenseInfo_Expired", outcome.RawErrorCode);
    }

    [Fact]
    public async Task MisaThrottled_after_single_retry_exhausted()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.MisaThrottled, "TooManyRequests"));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.MisaThrottled, outcome.Status);
    }

    [Fact]
    public async Task MisaUnavailable_after_single_retry_exhausted()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.MisaUnavailable, "ServiceUnavailable"));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.MisaUnavailable, outcome.Status);
    }

    [Fact]
    public async Task TransportFailed_on_no_response()
    {
        var stub = new StubClient(new MeInvoiceException(MeInvoiceErrorCategory.TransportFailed, "ConnectionRefused"));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.TransportFailed, outcome.Status);
    }

    [Fact]
    public async Task MisaUnknown_for_unmapped_errorCode()
    {
        var stub = new StubClient(new DeleteResponse(false, "WildcardNewCode", "Something odd happened."));
        var sut = MakeSut(stub, includeMessage: false);

        var outcome = await sut.ExecuteAsync(NewRequest(), default);

        Assert.Equal(DeleteDraftStatus.MisaUnknown, outcome.Status);
        Assert.Equal("WildcardNewCode", outcome.RawErrorCode);
    }

    [Fact]
    public async Task Khong_ton_tai_does_not_emit_drift_warning()
    {
        var stub = new StubClient(new DeleteResponse(false, "InvalidTransactionID", "RefID không tồn tại."));
        var logger = new CapturingLogger();
        var sut = MakeSut(stub, includeMessage: false, logger: logger);

        await sut.ExecuteAsync(NewRequest(), default);

        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("ARCH-DEL-001"));
    }

    private static DeleteDraftRequest NewRequest() =>
        new(RefId.From("abc-123"), InvoiceWithCode: true);

    private static DeleteDraftInvoice MakeSut(StubClient client, bool includeMessage, ILogger<DeleteDraftInvoice>? logger = null)
    {
        var ensure = new EnsureAccessToken(client, new InMemoryTokenCache(), new SystemClock(TimeProvider.System), () => "k");
        return new DeleteDraftInvoice(
            client,
            ensure,
            new InvalidTransactionDisambiguator(),
            new StubDeleteOptions(includeMessage),
            logger ?? NullLogger<DeleteDraftInvoice>.Instance);
    }

    private sealed class StubDeleteOptions : IDeleteOptionsAccessor
    {
        public StubDeleteOptions(bool include) => IncludeRawErrorMessage = include;
        public bool IncludeRawErrorMessage { get; }
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly DeleteResponse? _response;
        private readonly MeInvoiceException? _exception;
        public int DeleteCalls { get; private set; }

        public StubClient(DeleteResponse response) => _response = response;
        public StubClient(MeInvoiceException exception) => _exception = exception;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct) => Task.FromResult(new AccessToken("t", DateTimeOffset.UtcNow.AddDays(1)));
        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct)
        {
            DeleteCalls++;
            if (_exception is not null) throw _exception;
            return Task.FromResult(_response!);
        }
    }

    private sealed class CapturingLogger : ILogger<DeleteDraftInvoice>
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
