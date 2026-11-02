using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class SignExcelHappyPathFakeServerTests
{
    [Fact]
    public async Task End_to_end_excel_sign_populates_excelDocs_only_and_returns_signed_bytes()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignExcelAsync(TestPerFormatFixtures.SampleExcelRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedExcel);
        Assert.Equal(server.SignedExcelBytes, result.SignedExcel);
        Assert.Equal(DocumentFormat.Excel, result.Format);

        var hashReq = Assert.Single(server.HashRequests);
        Assert.Equal(RequestedFormat.Excel, hashReq.DetectedFormat);
        var attachReq = Assert.Single(server.AttachmentRequests);
        Assert.Equal(RequestedFormat.Excel, attachReq.DetectedFormat);
    }
}
