using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class SignXmlRequestValidatorTests
{
    private static SignXmlWorkRequest Valid() => new(
        Xml: "<root/>",
        SignatureContext: new XmlSignatureContext(
            SignatureName: "sig",
            HashAlgorithm: HashAlgorithm.SHA256,
            SignatureDescription: new SignatureDescription("a", "h", "t", "c")),
        DocumentId: "doc-1",
        DocumentName: "doc.xml",
        DataToBeDisplayed: "data");

    [Fact]
    public void Valid_request_passes()
    {
        var v = new SignXmlRequestValidator(new StubCorrelationIdAccessor("cid"));
        v.Validate(Valid());
    }

    [Fact]
    public void Empty_xml_rejected()
    {
        var v = new SignXmlRequestValidator(new StubCorrelationIdAccessor("cid"));
        var bad = Valid() with { Xml = "" };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(bad));
        Assert.Equal("InvalidSignXmlRequest", ex.RawCode);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }

    [Fact]
    public void Empty_signature_name_rejected()
    {
        var v = new SignXmlRequestValidator(new StubCorrelationIdAccessor("cid"));
        var bad = Valid() with
        {
            SignatureContext = Valid().SignatureContext with { SignatureName = "" }
        };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(bad));
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }
}
