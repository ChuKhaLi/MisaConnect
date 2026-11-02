using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Excel;

public class HashExcelDocumentTests
{
    private static Certificate Cert() => new(
        UserId: "u",
        KeyAlias: "ka",
        AppName: "misa",
        KeyStatus: KeyStatus.ACTIVE,
        CertStatus: "ACTIVE",
        CertificateValue: "BASE64CERT",
        CertificateChain: new CertificateChain("SIGN", "INT", "ROOT"),
        EffectiveDate: DateTimeOffset.UtcNow.AddYears(-1),
        ExpirationDate: DateTimeOffset.UtcNow.AddYears(1),
        EmailName: "alice@example.com",
        IsAutoSign: false);

    private static SignatureInfo Info() => new(
        SignatureName: "sig",
        HashAlgorithm: HashAlgorithm.SHA256,
        LogoImage: "logo",
        SignatureDescription: new SignatureDescription("a", "h", "t", "c"),
        RenderingMode: 1);

    [Fact]
    public async Task Returns_word_excel_hash_output_when_response_is_complete()
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnHashExcel = (_, _, _, _, _, _) => Task.FromResult(new WordExcelHashOutput("doc-1", "DOCB", "sig-e", "DIGEST", "MAIN")),
        };
        var sut = new HashExcelDocument(wire, correlation);
        var output = await sut.ExecuteAsync("tok", Cert(), new byte[] { 1 }, "doc-1", Info(), CancellationToken.None);
        Assert.Equal("sig-e", output.SignatureId);
    }

    [Fact]
    public async Task Surfaces_IncompleteHashResponse_with_excel_format_when_main_dom_missing()
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnHashExcel = (_, _, _, _, _, _) => Task.FromResult(new WordExcelHashOutput("doc-1", "DOCB", "sig-e", "DIGEST", "")),
        };
        var sut = new HashExcelDocument(wire, correlation);
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            sut.ExecuteAsync("tok", Cert(), new byte[] { 1 }, "doc-1", Info(), CancellationToken.None));
        Assert.Equal(DocumentFormat.Excel, ex.Format);
        Assert.Equal("IncompleteHashResponse", ex.RawCode);
    }
}
