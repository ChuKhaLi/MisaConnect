using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Validation;

public class SignPdfRequestValidatorTests
{
    private sealed class StubCorrelation : ICorrelationIdAccessor
    {
        public string Current => "cid-test";
    }

    private static SignPdfWorkRequest ValidRequest() => new(
        Pdf: new PdfDocument(new byte[] { 1, 2, 3 }),
        SignatureInfo: new SignatureInfo(
            SignatureName: "Signer",
            HashAlgorithm: HashAlgorithm.SHA256,
            LogoImage: "base64-logo",
            SignatureDescription: new SignatureDescription("Signer", "Hanoi", "Reason", "signer@example.com"),
            RenderingMode: 0),
        DocumentName: "doc.pdf",
        DocumentId: "11111111-1111-1111-1111-111111111111",
        DataToBeDisplayed: "<p>data</p>");

    [Fact]
    public void Valid_request_passes()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        v.Validate(ValidRequest());
    }

    [Fact]
    public void Empty_pdf_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var req = ValidRequest() with { Pdf = new PdfDocument(Array.Empty<byte>()) };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Document_name_over_100_chars_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var req = ValidRequest() with { DocumentName = new string('a', 101) };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Document_id_over_36_chars_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var req = ValidRequest() with { DocumentId = new string('a', 37) };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Missing_logo_image_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var bad = ValidRequest();
        var sigInfo = bad.SignatureInfo with { LogoImage = "" };
        var req = bad with { SignatureInfo = sigInfo };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Missing_signature_description_field_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var bad = ValidRequest();
        var desc = bad.SignatureInfo.SignatureDescription with { Reason = "" };
        var sigInfo = bad.SignatureInfo with { SignatureDescription = desc };
        var req = bad with { SignatureInfo = sigInfo };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Out_of_range_rendering_mode_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var bad = ValidRequest();
        var sigInfo = bad.SignatureInfo with { RenderingMode = 3 };
        var req = bad with { SignatureInfo = sigInfo };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Page_lt_1_rejected()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var bad = ValidRequest();
        var sigInfo = bad.SignatureInfo with { Page = 0 };
        var req = bad with { SignatureInfo = sigInfo };
        Assert.Throws<ESignGeneralException>(() => v.Validate(req));
    }

    [Fact]
    public void Validation_failure_carries_correlation_id()
    {
        var v = new SignPdfRequestValidator(new StubCorrelation());
        var req = ValidRequest() with { Pdf = new PdfDocument(Array.Empty<byte>()) };
        var ex = Assert.Throws<ESignGeneralException>(() => v.Validate(req));
        Assert.Equal("cid-test", ex.CorrelationId);
        Assert.Equal(ESignErrorCategory.Validation, ex.Category);
    }
}
