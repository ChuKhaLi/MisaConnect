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

    [Fact]
    public void Otp_code_field_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"userName\":\"alice\",\"code\":\"123456\",\"otpType\":0,\"remember\":true}");
        Assert.DoesNotContain("\"123456\"", scrubbed);
        Assert.Contains("alice", scrubbed);
    }

    [Fact]
    public void Password_field_replaced()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"userName\":\"alice\",\"password\":\"super-secret\"}");
        Assert.DoesNotContain("super-secret", scrubbed);
    }

    [Fact]
    public void Device_prefixed_field_replaced_defensively()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"deviceId\":\"device-1234567890\",\"deviceName\":\"My-Phone\"}");
        Assert.DoesNotContain("device-1234567890", scrubbed);
        Assert.DoesNotContain("My-Phone", scrubbed);
    }

    [Fact]
    public void MainDom_field_redacted_on_word_excel_responses()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"mainDom\":\"MAIN-DOM-SECRET\"}");
        Assert.DoesNotContain("MAIN-DOM-SECRET", scrubbed);
    }

    [Fact]
    public void SignatureId_field_redacted_on_per_format_responses()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"signatureId\":\"SIG-ID-SECRET\"}");
        Assert.DoesNotContain("SIG-ID-SECRET", scrubbed);
    }

    [Fact]
    public void Document_field_redacted_on_attachment_responses()
    {
        var scrubbed = ESignLogScrubber.Scrub("{\"document\":\"SIGNED-XML-CONTENT\"}");
        Assert.DoesNotContain("SIGNED-XML-CONTENT", scrubbed);
    }

    [Fact]
    public void Xml_word_excel_file_to_sign_redacted_on_hash_requests()
    {
        var hashReq = "{\"xmlDocs\":[{\"DocumentId\":\"doc-1\",\"FileToSign\":\"<root>secret-payload</root>\"}]}";
        var scrubbed = ESignLogScrubber.Scrub(hashReq);
        Assert.DoesNotContain("secret-payload", scrubbed);
    }

    [Fact]
    public void Per_format_digest_sh_documentBytes_documentHash_redacted()
    {
        var body = "{\"digest\":\"DIGEST-SECRET\",\"sh\":\"SH-SECRET\",\"documentBytes\":\"BYTES-SECRET\",\"documentHash\":\"HASH-SECRET\"}";
        var scrubbed = ESignLogScrubber.Scrub(body);
        Assert.DoesNotContain("DIGEST-SECRET", scrubbed);
        Assert.DoesNotContain("SH-SECRET", scrubbed);
        Assert.DoesNotContain("BYTES-SECRET", scrubbed);
        Assert.DoesNotContain("HASH-SECRET", scrubbed);
    }
}
