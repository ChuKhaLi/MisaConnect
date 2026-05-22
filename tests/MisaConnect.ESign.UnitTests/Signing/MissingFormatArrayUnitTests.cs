using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing;

public class MissingFormatArrayUnitTests
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
    [InlineData(DocumentFormat.Xml)]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public async Task Empty_signed_bytes_raises_MissingSignedDocument_with_format(DocumentFormat format)
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnAttachXml = (_, _, _, _, _) => Task.FromResult(Array.Empty<byte>()),
            OnAttachWordExcel = (_, _, _, _, _, _) => Task.FromResult(Array.Empty<byte>()),
        };
        if (format == DocumentFormat.Xml)
        {
            var sut = new AttachSignatureToXml(wire, correlation);
            var hash = new XmlHashOutput("doc-1", "<d/>", "sig", "D", "S");
            var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
                sut.ExecuteAsync("tok", Cert(), hash, "SIG", CancellationToken.None));
            Assert.Equal("MissingSignedDocument", ex.RawCode);
            Assert.Equal(format, ex.Format);
        }
        else
        {
            var sut = new AttachSignatureToWordExcel(wire, correlation);
            var hash = new WordExcelHashOutput("doc-1", "DOCB", "sig", "D", "MAIN");
            var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
                sut.ExecuteAsync("tok", Cert(), hash, "SIG", format, CancellationToken.None));
            Assert.Equal("MissingSignedDocument", ex.RawCode);
            Assert.Equal(format, ex.Format);
        }
    }
}
