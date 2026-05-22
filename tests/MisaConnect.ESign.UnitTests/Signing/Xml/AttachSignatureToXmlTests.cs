using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class AttachSignatureToXmlTests
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

    [Fact]
    public async Task Returns_signed_xml_bytes_when_wire_returns_non_empty()
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var expected = System.Text.Encoding.UTF8.GetBytes("<signed/>");
        var wire = new StubWireClient
        {
            OnAttachXml = (_, _, _, _, _) => Task.FromResult(expected),
        };
        var sut = new AttachSignatureToXml(wire, correlation);
        var hash = new XmlHashOutput("doc-1", "<d/>", "sig-x", "D", "S");
        var bytes = await sut.ExecuteAsync("tok", Cert(), hash, "SIG-BYTES", CancellationToken.None);
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public async Task Surfaces_MissingSignedDocument_when_wire_returns_empty_bytes()
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnAttachXml = (_, _, _, _, _) => Task.FromResult(Array.Empty<byte>()),
        };
        var sut = new AttachSignatureToXml(wire, correlation);
        var hash = new XmlHashOutput("doc-1", "<d/>", "sig-x", "D", "S");
        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            sut.ExecuteAsync("tok", Cert(), hash, "SIG-BYTES", CancellationToken.None));
        Assert.Equal(ESignErrorCategory.AttachmentRejected, ex.Category);
        Assert.Equal("MissingSignedDocument", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }
}
