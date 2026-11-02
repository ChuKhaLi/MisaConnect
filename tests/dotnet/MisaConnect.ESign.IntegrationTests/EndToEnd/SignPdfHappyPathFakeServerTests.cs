using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class SignPdfHappyPathFakeServerTests
{
    [Fact]
    public async Task End_to_end_returns_signed_pdf_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedPdf);
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
