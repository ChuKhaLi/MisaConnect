using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.Certificates;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Certificates;

public class CertificateSelectorTests
{
    private static Certificate MakeCert(string keyAlias, KeyStatus status) => new(
        UserId: "u1",
        KeyAlias: keyAlias,
        AppName: "app",
        KeyStatus: status,
        CertStatus: status.ToString(),
        CertificateValue: "cert",
        CertificateChain: new CertificateChain("s", "i", "r"),
        EffectiveDate: null,
        ExpirationDate: null,
        EmailName: null,
        IsAutoSign: false);

    [Fact]
    public async Task First_active_in_order_is_selected()
    {
        var selector = new FirstActiveCertificateSelector();
        var picked = await selector.SelectAsync(new[]
        {
            MakeCert("alpha", KeyStatus.ACTIVE),
            MakeCert("beta", KeyStatus.ACTIVE),
        }, CancellationToken.None);
        Assert.Equal("alpha", picked.KeyAlias);
    }

    [Fact]
    public async Task ListActiveCertificates_filters_to_active_only()
    {
        var wire = new StubWireClient
        {
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[]
            {
                MakeCert("inactive-1", KeyStatus.INACTIVE),
                MakeCert("active-1", KeyStatus.ACTIVE),
                MakeCert("inactive-2", KeyStatus.INACTIVE),
                MakeCert("active-2", KeyStatus.ACTIVE),
            }),
        };
        var listActive = new ListActiveCertificates(wire, new StubCorrelationIdAccessor());

        var result = await listActive.ExecuteAsync("token", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("active-1", result[0].KeyAlias);
        Assert.Equal("active-2", result[1].KeyAlias);
    }

    [Fact]
    public async Task ListActiveCertificates_throws_when_none_active()
    {
        var wire = new StubWireClient
        {
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(new[]
            {
                MakeCert("inactive-1", KeyStatus.INACTIVE),
                MakeCert("inactive-2", KeyStatus.INACTIVE),
            }),
        };
        var listActive = new ListActiveCertificates(wire, new StubCorrelationIdAccessor("cid-x"));

        var ex = await Assert.ThrowsAsync<NoActiveCertificateException>(() =>
            listActive.ExecuteAsync("token", CancellationToken.None));
        Assert.Equal("cid-x", ex.CorrelationId);
    }

    [Fact]
    public async Task ListActiveCertificates_throws_when_empty()
    {
        var wire = new StubWireClient
        {
            OnListCerts = (_, _) => Task.FromResult<IReadOnlyList<Certificate>>(Array.Empty<Certificate>()),
        };
        var listActive = new ListActiveCertificates(wire, new StubCorrelationIdAccessor());

        await Assert.ThrowsAsync<NoActiveCertificateException>(() =>
            listActive.ExecuteAsync("token", CancellationToken.None));
    }
}
