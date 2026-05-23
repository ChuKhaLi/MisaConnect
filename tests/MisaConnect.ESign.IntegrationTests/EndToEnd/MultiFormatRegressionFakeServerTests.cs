using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// T106 — re-runs slice-3's per-format happy paths after the T023-T025
/// BeginSign{Format}Core refactor; verifies blocking facades for XML, Word,
/// Excel still call /Signing/hash → status → attachment and produce byte-
/// identical results (FR-091 / SC-032).
/// </summary>
public class MultiFormatRegressionFakeServerTests
{
    [Fact]
    public async Task SignXml_polling_path_remains_byte_identical_after_slice4_refactor()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignXmlAsync(TestPerFormatFixtures.SampleXmlRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedXml);
        Assert.Equal(1, server.Calls.Hash);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.True(server.Calls.SignStatus >= 2);
    }

    [Fact]
    public async Task SignWord_polling_path_remains_byte_identical_after_slice4_refactor()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignWordAsync(TestPerFormatFixtures.SampleWordRequest(), CancellationToken.None);

        Assert.Equal(server.SignedWordBytes, result.SignedWord);
        Assert.Equal(1, server.Calls.Hash);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.True(server.Calls.SignStatus >= 2);
    }

    [Fact]
    public async Task SignExcel_polling_path_remains_byte_identical_after_slice4_refactor()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None);

        Assert.Equal(server.SignedExcelBytes, result.SignedExcel);
        Assert.Equal(1, server.Calls.Hash);
        Assert.Equal(1, server.Calls.Attachment);
        Assert.True(server.Calls.SignStatus >= 2);
    }
}
