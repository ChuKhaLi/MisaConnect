using MisaConnect.ESign.Infrastructure.Logging;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Logging;

public class ESignLogScrubberWebhookTests
{
    [Fact]
    public void Scrub_redacts_inbound_signature_field()
    {
        var input = "{\"signatures\":[{\"documentId\":\"doc-1\",\"signature\":\"SECRET-SIGNATURE-BYTES\"}]}";
        var scrubbed = ESignLogScrubber.Scrub(input);
        Assert.DoesNotContain("SECRET-SIGNATURE-BYTES", scrubbed);
    }

    [Fact]
    public void Scrub_redacts_extraData_object_body()
    {
        var input = "{\"messageId\":\"m1\",\"extraData\":{\"customerName\":\"Alice\",\"sensitive\":\"value\"},\"transactionId\":\"tx\"}";
        var scrubbed = ESignLogScrubber.Scrub(input);
        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("\"sensitive\":\"value\"", scrubbed);
    }

    [Fact]
    public void Scrub_redacts_secret_field_when_present_in_json()
    {
        var input = "{\"webhook\":{\"secret\":\"abcdef1234567890abcdef1234567890\"}}";
        var scrubbed = ESignLogScrubber.Scrub(input);
        Assert.DoesNotContain("abcdef1234567890abcdef1234567890", scrubbed);
    }
}
