using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Errors;

public class PerFormatErrorMapperTests
{
    [Fact]
    public void Synthesizes_InvalidXmlInput_when_hash_xml_format_with_malformed_xml_msg()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "malformed xml", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointHash,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid",
            requestedFormat: DocumentFormat.Xml);
        Assert.Equal("InvalidXmlInput", ex.RawCode);
        Assert.Equal(ESignErrorCategory.HashRejected, ex.Category);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }

    [Fact]
    public void Synthesizes_MissingMainDom_on_attachment_for_word_when_main_dom_msg()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "mainDom missing", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointAttachment,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid",
            requestedFormat: DocumentFormat.Word);
        Assert.Equal("MissingMainDom", ex.RawCode);
        Assert.Equal(ESignErrorCategory.AttachmentRejected, ex.Category);
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }

    [Fact]
    public void Synthesizes_MissingMainDom_on_attachment_for_excel_when_main_dom_msg()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "missing main dom field", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointAttachment,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid",
            requestedFormat: DocumentFormat.Excel);
        Assert.Equal("MissingMainDom", ex.RawCode);
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }

    [Theory]
    [InlineData(DocumentFormat.Xml)]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public void Synthesizes_MissingSignatureId_on_attachment_when_signature_id_missing(DocumentFormat fmt)
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "signatureId is missing", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointAttachment,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid",
            requestedFormat: fmt);
        Assert.Equal("MissingSignatureId", ex.RawCode);
        Assert.Equal(fmt, ex.Format);
    }

    [Fact]
    public void Synthesizes_UnsupportedDocumentVariant_on_unsupported_msg()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "format not supported", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointHash,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid",
            requestedFormat: DocumentFormat.Excel);
        Assert.Equal("UnsupportedDocumentVariant", ex.RawCode);
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }

    [Fact]
    public void Default_format_pdf_preserves_slice1_mapping()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: null, DevMsg: "Document parse failed", UserMsg: null);
        var ex = (ESignGeneralException)ESignErrorMapper.Map(
            endpoint: ESignErrorMapper.EndpointHash,
            statusCode: 400,
            envelope: envelope,
            correlationId: "cid");
        Assert.Equal("InvalidDocument", ex.RawCode);
        Assert.Equal(DocumentFormat.Pdf, ex.Format);
    }
}
