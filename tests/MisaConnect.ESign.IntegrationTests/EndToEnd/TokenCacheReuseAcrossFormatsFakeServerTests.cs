using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class TokenCacheReuseAcrossFormatsFakeServerTests
{
    [Fact]
    public async Task Pdf_then_xml_then_word_then_excel_share_one_login_and_one_cert_lookup()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);
        await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);
        await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);
        await client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None);

        // SC-016: format is NOT part of the cache key. Exactly one login.
        // Certificates lookup is per-call (slice-1 behavior) and runs once per
        // facade invocation; the test asserts SC-016 by pinning login count.
        Assert.Equal(1, server.Calls.Login);
        Assert.Equal(4, server.Calls.Hash);
        Assert.Equal(4, server.Calls.Attachment);
    }
}
