using System.Text.Json;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.ESign.Mapping;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Xml;

public class XmlSignatureContextMapperTests
{
    [Fact]
    public void Fills_misa_defaults_for_no_signature_visualization()
    {
        var ctx = new XmlSignatureContext(
            SignatureName: "sig",
            HashAlgorithm: HashAlgorithm.SHA256,
            SignatureDescription: new SignatureDescription("a", "h", "t", "c"));

        var wire = XmlSignatureContextMapper.ToWireSignatureInfo(ctx);

        Assert.Equal("sig", wire.SignatureName);
        Assert.Equal("SHA256", wire.HashAlgorithm);
        Assert.Equal(string.Empty, wire.LogoImage);
        Assert.Equal(0, wire.RenderingMode);
        Assert.Null(wire.TextColor);
        Assert.Null(wire.PositionX);
        Assert.Null(wire.PositionY);
        Assert.Null(wire.Width);
        Assert.Null(wire.Height);
        Assert.Null(wire.FontSize);
        Assert.Null(wire.FontData);
        Assert.Null(wire.SignatureImage);
        Assert.Null(wire.Page);
        Assert.Null(wire.SignaturePosInfos);
    }

    [Fact]
    public void Json_serialization_omits_null_visual_fields()
    {
        var ctx = new XmlSignatureContext(
            SignatureName: "sig",
            HashAlgorithm: HashAlgorithm.SHA256,
            SignatureDescription: new SignatureDescription("a", "h", "t", "c"));

        var wire = XmlSignatureContextMapper.ToWireSignatureInfo(ctx);
        var opts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(wire, opts);

        Assert.DoesNotContain("\"PositionX\"", json);
        Assert.DoesNotContain("\"FontData\"", json);
        Assert.DoesNotContain("\"SignaturePosInfos\"", json);
        Assert.Contains("\"SignatureName\":\"sig\"", json);
        Assert.Contains("\"RenderingMode\":0", json);
    }
}
