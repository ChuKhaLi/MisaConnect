using System.Text.Json;
using System.Text.Json.Serialization;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Signing.Wire;

public class LooseNullableBooleanConverterTests
{
    private sealed class Holder
    {
        [JsonPropertyName("error")]
        [JsonConverter(typeof(LooseNullableBooleanConverter))]
        public bool? Error { get; set; }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"FALSE\"", false)]
    [InlineData("\" true \"", true)]
    [InlineData("null", null)]
    [InlineData("\"yes\"", null)]
    [InlineData("1", null)]
    public void Reads_loosely(string raw, bool? expected)
    {
        var holder = JsonSerializer.Deserialize<Holder>($"{{\"error\":{raw},\"next\":1}}");

        Assert.NotNull(holder);
        Assert.Equal(expected, holder!.Error);
    }

    [Theory]
    [InlineData(true, "{\"error\":true}")]
    [InlineData(false, "{\"error\":false}")]
    [InlineData(null, "{\"error\":null}")]
    public void Writes_boolean_or_null(bool? value, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(new Holder { Error = value }));
    }
}
