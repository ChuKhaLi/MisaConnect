using MisaConnect.ESign.Client.Dtos;
using MisaConnect.ESign.Client.Mapping;
using MisaConnect.ESign.Domain.Signing;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class SignXmlRequestMapperTests
{
    private static XmlSignatureContextDto Ctx(string hashAlgo = "SHA256") => new(
        SignatureName: "sig",
        HashAlgorithm: hashAlgo,
        SignatureDescription: new SignatureDescriptionDto("a", "h", "t", "c"));

    [Fact]
    public void Maps_string_xml_overload_to_work_request_verbatim()
    {
        var dto = SignXmlRequest.FromString("<root/>", Ctx(), "doc.xml", "data");
        var wr = SignXmlRequestMapper.ToWorkRequest(dto);
        Assert.Equal("<root/>", wr.Xml);
        Assert.Equal(HashAlgorithm.SHA256, wr.SignatureContext.HashAlgorithm);
        Assert.False(string.IsNullOrEmpty(wr.DocumentId));
    }

    [Fact]
    public void Maps_bytes_xml_overload_via_utf8_decode()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("<root/>");
        var dto = SignXmlRequest.FromUtf8Bytes(bytes, Ctx(), "doc.xml", "data");
        var wr = SignXmlRequestMapper.ToWorkRequest(dto);
        Assert.Equal("<root/>", wr.Xml);
    }

    [Fact]
    public void Both_xml_and_bytes_null_throws_ArgumentException()
    {
        var dto = new SignXmlRequestDto(null, null, Ctx(), "doc.xml", "data");
        Assert.Throws<ArgumentException>(() => SignXmlRequestMapper.ToWorkRequest(dto));
    }

    [Fact]
    public void Both_xml_and_bytes_set_throws_ArgumentException()
    {
        var dto = new SignXmlRequestDto("<r/>", new byte[] { 1 }, Ctx(), "doc.xml", "data");
        Assert.Throws<ArgumentException>(() => SignXmlRequestMapper.ToWorkRequest(dto));
    }

    [Fact]
    public void Empty_hash_algo_defaults_to_sha256()
    {
        var dto = SignXmlRequest.FromString("<root/>", Ctx(""), "doc.xml", "data");
        var wr = SignXmlRequestMapper.ToWorkRequest(dto);
        Assert.Equal(HashAlgorithm.SHA256, wr.SignatureContext.HashAlgorithm);
    }

    [Fact]
    public void Supplied_document_id_is_preserved()
    {
        var dto = SignXmlRequest.FromString("<r/>", Ctx(), "doc.xml", "data", documentId: "fixed-id");
        var wr = SignXmlRequestMapper.ToWorkRequest(dto);
        Assert.Equal("fixed-id", wr.DocumentId);
    }
}
