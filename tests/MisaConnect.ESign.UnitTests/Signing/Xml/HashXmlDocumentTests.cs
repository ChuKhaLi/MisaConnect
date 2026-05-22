using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Certificates;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class HashXmlDocumentTests
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

    private static XmlSignatureContext Ctx() => new(
        SignatureName: "sig",
        HashAlgorithm: HashAlgorithm.SHA256,
        SignatureDescription: new SignatureDescription("a", "h", "t", "c"));

    [Fact]
    public async Task Returns_xml_hash_output_when_response_is_complete()
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnHashXml = (_, _, _, _, _, _) => Task.FromResult(new XmlHashOutput("doc-1", "<d/>", "sig-x", "DIGEST", "SH")),
        };
        var sut = new HashXmlDocument(wire, correlation);

        var output = await sut.ExecuteAsync("tok", Cert(), "<root/>", "doc-1", Ctx(), CancellationToken.None);

        Assert.Equal("doc-1", output.DocumentId);
        Assert.Equal("<d/>", output.Document);
        Assert.Equal("sig-x", output.SignatureId);
        Assert.Equal("DIGEST", output.Digest);
        Assert.Equal("SH", output.Sh);
    }

    [Theory]
    [InlineData("", "sig-x", "DIGEST", "SH")]
    [InlineData("<d/>", "", "DIGEST", "SH")]
    [InlineData("<d/>", "sig-x", "", "SH")]
    [InlineData("<d/>", "sig-x", "DIGEST", "")]
    public async Task Surfaces_IncompleteHashResponse_when_any_required_field_missing(string doc, string sigId, string digest, string sh)
    {
        var correlation = new StubCorrelationIdAccessor("cid");
        var wire = new StubWireClient
        {
            OnHashXml = (_, _, _, _, _, _) => Task.FromResult(new XmlHashOutput("doc-1", doc, sigId, digest, sh)),
        };
        var sut = new HashXmlDocument(wire, correlation);

        var ex = await Assert.ThrowsAsync<ESignGeneralException>(() =>
            sut.ExecuteAsync("tok", Cert(), "<root/>", "doc-1", Ctx(), CancellationToken.None));

        Assert.Equal(ESignErrorCategory.HashRejected, ex.Category);
        Assert.Equal("IncompleteHashResponse", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }
}
