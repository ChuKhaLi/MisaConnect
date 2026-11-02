using Microsoft.Extensions.Time.Testing;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Domain.Pdf;
using MisaConnect.EInvoice.Domain.Templates;
using MisaConnect.EInvoice.Infrastructure.Caching;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.UseCases;

public class EnsureAccessTokenTests
{
    private const string CacheKey = "tax-app";

    [Fact]
    public async Task Cache_miss_acquires_and_caches_with_14d_minus_1h_expiry()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryTokenCache();
        var client = new StubClient(new AccessToken("token-A", DateTimeOffset.MinValue));
        var sut = new EnsureAccessToken(client, cache, new SystemClock(clock), () => CacheKey);

        var result = await sut.ExecuteAsync(default);

        Assert.Equal("token-A", result.Value);
        var expected = clock.GetUtcNow() + TimeSpan.FromDays(14) - TimeSpan.FromHours(1);
        Assert.Equal(expected, result.ExpiresAtUtc);
        Assert.Equal(1, client.AcquireCount);
    }

    [Fact]
    public async Task Cache_hit_within_validity_skips_MISA()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryTokenCache();
        await cache.SetAsync(CacheKey, new AccessToken("cached", clock.GetUtcNow().AddDays(7)), default);
        var client = new StubClient(new AccessToken("should-not-call", DateTimeOffset.MaxValue));
        var sut = new EnsureAccessToken(client, cache, new SystemClock(clock), () => CacheKey);

        var result = await sut.ExecuteAsync(default);
        Assert.Equal("cached", result.Value);
        Assert.Equal(0, client.AcquireCount);
    }

    [Fact]
    public async Task Cache_hit_within_skew_window_reacquires()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryTokenCache();
        await cache.SetAsync(CacheKey, new AccessToken("about-to-expire", clock.GetUtcNow().AddMinutes(30)), default);
        var client = new StubClient(new AccessToken("fresh", DateTimeOffset.MinValue));
        var sut = new EnsureAccessToken(client, cache, new SystemClock(clock), () => CacheKey);

        var result = await sut.ExecuteAsync(default);
        Assert.Equal("fresh", result.Value);
        Assert.Equal(1, client.AcquireCount);
    }

    private sealed class StubClient : IMeInvoiceClient
    {
        private readonly AccessToken _next;
        public int AcquireCount { get; private set; }

        public StubClient(AccessToken next) => _next = next;

        public Task<AccessToken> AcquireTokenAsync(CancellationToken ct)
        {
            AcquireCount++;
            return Task.FromResult(_next);
        }

        public Task<IReadOnlyList<Template>> ListTemplatesAsync(bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> PreviewAsync(Invoice invoice, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<SaveResult>> SaveDraftAsync(BatchSubmission batch, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<PdfDocument> GetDraftPdfByRefIdAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<DeleteResponse> DeleteDraftAsync(RefId refId, bool invoiceWithCode, CancellationToken ct) => throw new NotImplementedException();
    }
}
