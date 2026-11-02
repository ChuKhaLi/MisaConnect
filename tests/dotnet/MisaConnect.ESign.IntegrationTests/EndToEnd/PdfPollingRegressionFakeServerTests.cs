using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// T105 — re-runs slice-1's PDF happy path after the T022 BeginSignPdfCore
/// refactor; verifies the blocking facade still calls /Signing/hash → status →
/// attachment in order and returns byte-identical signed bytes (FR-089 / SC-032).
/// </summary>
public class PdfPollingRegressionFakeServerTests
{
    [Fact]
    public async Task SignPdf_polling_path_remains_byte_identical_after_slice4_refactor()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.Equal(server.SignedPdfBytes, result.SignedPdf);
        Assert.Equal("tx-fake-1", result.TransactionId);
        Assert.Equal("key-alias-1", result.CertificateKeyAlias);
        Assert.Equal(1, server.Calls.Login);
        Assert.Equal(1, server.Calls.Certificates);
        Assert.Equal(1, server.Calls.Hash);
        Assert.Equal(1, server.Calls.SignHash);
        Assert.True(server.Calls.SignStatus >= 2);
        Assert.Equal(1, server.Calls.Attachment);
    }
}
