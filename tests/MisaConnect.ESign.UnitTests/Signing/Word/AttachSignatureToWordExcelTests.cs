using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Word;

public class AttachSignatureToWordExcelTests
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

    [Theory]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public async Task Returns_signed_bytes_when_wire_returns_non_empty(DocumentFormat format)
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var expected = new byte[] { 0x50, 0x4B };
        var wire = new StubWireClient
        {
            OnAttachWordExcel = (_, _, _, _, _, _) => Task.FromResult(expected),
        };
        var sut = new AttachSignatureToWordExcel(wire, correlation);
        var hash = new WordExcelHashOutput("doc-1", "DOCB", "sig", "D", "MAIN");
        var bytes = await sut.ExecuteAsync("tok", Cert(), hash, "SIG", format, CancellationToken.None);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public async Task Surfaces_MissingSignedDocument_with_requested_format(DocumentFormat format)
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnAttachWordExcel = (_, _, _, _, _, _) => Task.FromResult(Array.Empty<byte>()),
        };
        var sut = new AttachSignatureToWordExcel(wire, correlation);
        var hash = new WordExcelHashOutput("doc-1", "DOCB", "sig", "D", "MAIN");
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            sut.ExecuteAsync("tok", Cert(), hash, "SIG", format, CancellationToken.None));
        Assert.Equal("MissingSignedDocument", ex.RawCode);
        Assert.Equal(format, ex.Format);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xml)]
    [InlineData(DocumentFormat.Unknown)]
    public async Task Rejects_non_word_excel_format(DocumentFormat format)
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient();
        var sut = new AttachSignatureToWordExcel(wire, correlation);
        var hash = new WordExcelHashOutput("doc-1", "DOCB", "sig", "D", "MAIN");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ExecuteAsync("tok", Cert(), hash, "SIG", format, CancellationToken.None));
    }
}
