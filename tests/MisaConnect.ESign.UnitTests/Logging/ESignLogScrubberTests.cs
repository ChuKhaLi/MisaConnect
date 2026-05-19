using MisaConnect.ESign.Infrastructure.Logging;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Logging;

public class ESignLogScrubberTests
{
    [Fact]
    public void Bearer_token_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("Authorization: Bearer eyJABCDEFGHIJKLMNOPQ");
        Assert.DoesNotContain("eyJABCDEFGHIJKLMNOPQ", scrubbed);
    }

    [Fact]
    public void AuthorizationRM_header_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("AuthorizationRM: secret-token-value-1234567890");
        Assert.DoesNotContain("secret-token-value", scrubbed);
    }

    [Fact]
    public void Refresh_token_json_field_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"refreshToken\":\"abcdef1234567890\"}");
        Assert.DoesNotContain("abcdef1234567890", scrubbed);
    }

    [Fact]
    public void Pii_email_field_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"email\":\"user@example.com\"}");
        Assert.DoesNotContain("user@example.com", scrubbed);
    }

    [Fact]
    public void Pii_phone_field_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"phoneNumber\":\"0987654321\"}");
        Assert.DoesNotContain("0987654321", scrubbed);
    }

    [Fact]
    public void Long_base64_chunk_replaced()
    {
        var b64 = Convert.ToBase64String(new byte[40]);
        var scrubbed = ESignLogScrubber.Scrub($"chunk={b64}");
        Assert.DoesNotContain(b64, scrubbed);
    }

    [Fact]
    public void Correlation_id_and_endpoint_preserved()
    {
        var scrubbed = ESignLogScrubber.Scrub("endpoint=Signing/hash correlationId=cid-abc duration=42ms");
        Assert.Contains("cid-abc", scrubbed);
        Assert.Contains("Signing/hash", scrubbed);
        Assert.Contains("42", scrubbed);
    }
}
