using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Webhook;
using MisaConnect.ESign.Infrastructure.Caching;
using MisaConnect.ESign.UnitTests.TestSupport;
using MisaConnect.ESign.UnitTests.Webhook;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Compatibility;

/// <summary>
/// FR-019: the 2.4 constructors of <see cref="HandleWebhook"/> and the <c>BeginSign*</c> use
/// cases stay available (source and binary) as compatibility overloads. Each test constructs
/// the type with the exact 2.4 argument list and checks that a constructor with exactly the
/// 2.4 parameter types exists, which is what a caller compiled against 2.4 binds to.
/// </summary>
public class UseCaseConstructorCompatibilityTests
{
    private static readonly FakeClock Clock = new(DateTimeOffset.UtcNow);
    private static readonly StubWireClient Wire = new();
    private static readonly StubCorrelationIdAccessor Correlation = new("cid-test");

    private static EnsureAccessToken Ensure() =>
        new(Wire, new InMemoryTokenCache(), new BeginSignFixtures.StaticKeySelector(), Clock, () => ("alice", "pass"));

    private static Type[] BeginSign24Parameters<TUseCase, THash, TValidator>() => new[]
    {
        typeof(EnsureAccessToken), typeof(ListActiveCertificates), typeof(ICertificateSelector),
        typeof(THash), typeof(SubmitSignHash), typeof(ISigningSessionStore), typeof(ISystemClock),
        typeof(TValidator), typeof(Func<string>), typeof(Func<TimeSpan>), typeof(ILogger<TUseCase>),
        typeof(IOtpProvider), typeof(ExchangeOtp), typeof(OtpSubmissionValidator), typeof(ICorrelationIdAccessor),
    };

    [Fact]
    public void BeginSignPdf_keeps_its_2_4_constructor()
    {
        var sut = new BeginSignPdf(
            Ensure(), new ListActiveCertificates(Wire, Correlation), new BeginSignFixtures.FirstCertSelector(),
            new HashPdfDocument(Wire, Correlation), new SubmitSignHash(Wire), new BeginSignFixtures.StubSigningSessionStore(),
            Clock, new SignPdfRequestValidator(Correlation), () => "client-1", () => TimeSpan.FromHours(24),
            NullLogger<BeginSignPdf>.Instance, null, null, null, Correlation);

        Assert.NotNull(sut);
        Assert.NotNull(typeof(BeginSignPdf).GetConstructor(
            BeginSign24Parameters<BeginSignPdf, HashPdfDocument, SignPdfRequestValidator>()));
    }

    [Fact]
    public void BeginSignXml_keeps_its_2_4_constructor()
    {
        var sut = new BeginSignXml(
            Ensure(), new ListActiveCertificates(Wire, Correlation), new BeginSignFixtures.FirstCertSelector(),
            new HashXmlDocument(Wire, Correlation), new SubmitSignHash(Wire), new BeginSignFixtures.StubSigningSessionStore(),
            Clock, new SignXmlRequestValidator(Correlation), () => "client-1", () => TimeSpan.FromHours(24),
            NullLogger<BeginSignXml>.Instance, null, null, null, Correlation);

        Assert.NotNull(sut);
        Assert.NotNull(typeof(BeginSignXml).GetConstructor(
            BeginSign24Parameters<BeginSignXml, HashXmlDocument, SignXmlRequestValidator>()));
    }

    [Fact]
    public void BeginSignWord_keeps_its_2_4_constructor()
    {
        var sut = new BeginSignWord(
            Ensure(), new ListActiveCertificates(Wire, Correlation), new BeginSignFixtures.FirstCertSelector(),
            new HashWordDocument(Wire, Correlation), new SubmitSignHash(Wire), new BeginSignFixtures.StubSigningSessionStore(),
            Clock, new SignWordRequestValidator(Correlation), () => "client-1", () => TimeSpan.FromHours(24),
            NullLogger<BeginSignWord>.Instance, null, null, null, Correlation);

        Assert.NotNull(sut);
        Assert.NotNull(typeof(BeginSignWord).GetConstructor(
            BeginSign24Parameters<BeginSignWord, HashWordDocument, SignWordRequestValidator>()));
    }

    [Fact]
    public void BeginSignExcel_keeps_its_2_4_constructor()
    {
        var sut = new BeginSignExcel(
            Ensure(), new ListActiveCertificates(Wire, Correlation), new BeginSignFixtures.FirstCertSelector(),
            new HashExcelDocument(Wire, Correlation), new SubmitSignHash(Wire), new BeginSignFixtures.StubSigningSessionStore(),
            Clock, new SignExcelRequestValidator(Correlation), () => "client-1", () => TimeSpan.FromHours(24),
            NullLogger<BeginSignExcel>.Instance, null, null, null, Correlation);

        Assert.NotNull(sut);
        Assert.NotNull(typeof(BeginSignExcel).GetConstructor(
            BeginSign24Parameters<BeginSignExcel, HashExcelDocument, SignExcelRequestValidator>()));
    }

    [Fact]
    public void HandleWebhook_keeps_its_2_4_constructor()
    {
        var store = new BeginSignFixtures.StubSigningSessionStore();
        var hook = new NoopHook();
        var finalize = BeginSignFixtures.BuildFinalize(new StubWireClient(), store, new NoopLockOwner(), hook, Clock);

        var sut = new HandleWebhook(
            new WebhookEnvelopeValidator(), store, finalize, hook, Correlation,
            () => "client-1", () => "0", NullLogger<HandleWebhook>.Instance);

        Assert.NotNull(sut);
        Assert.NotNull(typeof(HandleWebhook).GetConstructor(new[]
        {
            typeof(WebhookEnvelopeValidator), typeof(ISigningSessionStore), typeof(FinalizeFromWebhook),
            typeof(IWebhookDeliveryHook), typeof(ICorrelationIdAccessor), typeof(Func<string>),
            typeof(Func<string>), typeof(ILogger<HandleWebhook>),
        }));
    }

    private sealed class NoopHook : IWebhookDeliveryHook
    {
        public Task DeliverAsync(WebhookOutcome outcome, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoopLockOwner : IFinalizeLockOwner
    {
        public ValueTask<IAsyncDisposable> AcquireFinalizeLockAsync(string clientId, string transactionId, CancellationToken ct) =>
            ValueTask.FromResult<IAsyncDisposable>(new NoopLock());
    }

    private sealed class NoopLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
