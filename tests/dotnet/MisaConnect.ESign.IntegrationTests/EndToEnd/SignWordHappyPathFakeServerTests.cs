using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class SignWordHappyPathFakeServerTests
{
    [Fact]
    public async Task End_to_end_word_sign_populates_wordDocs_only_and_returns_signed_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedWord);
        Assert.Equal(server.SignedWordBytes, result.SignedWord);
        Assert.Equal(DocumentFormat.Word, result.Format);
        Assert.Equal("tx-fake-1", result.TransactionId);

        var hashReq = Assert.Single(server.HashRequests);
        Assert.Equal(RequestedFormat.Word, hashReq.DetectedFormat);
        var attachReq = Assert.Single(server.AttachmentRequests);
        Assert.Equal(RequestedFormat.Word, attachReq.DetectedFormat);
    }
}
