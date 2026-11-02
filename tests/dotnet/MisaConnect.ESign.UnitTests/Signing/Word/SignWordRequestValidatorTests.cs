using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Word;

public class SignWordRequestValidatorTests
{
    private static SignWordWorkRequest Valid() => new(
        Word: new byte[] { 1 },
        SignatureInfo: new SignatureInfo(
            SignatureName: "sig",
            HashAlgorithm: HashAlgorithm.SHA256,
            LogoImage: "logo",
            SignatureDescription: new SignatureDescription("a", "h", "t", "c"),
            RenderingMode: 1),
        DocumentId: "doc-1",
        DocumentName: "doc.docx",
        DataToBeDisplayed: "data");

    [Fact]
    public void Valid_request_passes() =>
        new SignWordRequestValidator(new StubCorrelationIdAccessor("cid")).Validate(Valid());

    [Fact]
    public void Empty_bytes_rejected()
    {
        var v = new SignWordRequestValidator(new StubCorrelationIdAccessor("cid"));
        var bad = Valid() with { Word = Array.Empty<byte>() };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(bad));
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }

    [Fact]
    public void Empty_signature_name_rejected()
    {
        var v = new SignWordRequestValidator(new StubCorrelationIdAccessor("cid"));
        var bad = Valid() with { SignatureInfo = Valid().SignatureInfo with { SignatureName = "" } };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(bad));
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }
}
