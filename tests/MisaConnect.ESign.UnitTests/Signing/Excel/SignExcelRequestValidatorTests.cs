using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Excel;

public class SignExcelRequestValidatorTests
{
    private static SignExcelWorkRequest Valid() => new(
        Excel: new byte[] { 1 },
        SignatureInfo: new SignatureInfo(
            SignatureName: "sig",
            HashAlgorithm: HashAlgorithm.SHA256,
            LogoImage: "logo",
            SignatureDescription: new SignatureDescription("a", "h", "t", "c"),
            RenderingMode: 1),
        DocumentId: "doc-1",
        DocumentName: "doc.xlsx",
        DataToBeDisplayed: "data");

    [Fact]
    public void Valid_request_passes() =>
        new SignExcelRequestValidator(new StubCorrelationIdAccessor("cid")).Validate(Valid());

    [Fact]
    public void Empty_bytes_rejected()
    {
        var v = new SignExcelRequestValidator(new StubCorrelationIdAccessor("cid"));
        var bad = Valid() with { Excel = Array.Empty<byte>() };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(bad));
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }
}
