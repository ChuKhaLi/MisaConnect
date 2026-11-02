using System.Collections.Generic;
using MisaConnect.EInvoice.Infrastructure.Logging;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Logging;

public class MeInvoiceLogScrubberTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("Bearer")]
    [InlineData("Taxcode")]
    [InlineData("Password")]
    [InlineData("Token")]
    public void Sensitive_header_values_are_redacted(string sensitiveKey)
    {
        var headers = new Dictionary<string, string?>
        {
            [sensitiveKey] = "this-is-secret",
            ["Content-Type"] = "application/json",
        };

        MeInvoiceLogScrubber.Redact(headers);

        Assert.Equal("***", headers[sensitiveKey]);
        Assert.Equal("application/json", headers["Content-Type"]);
    }

    [Fact]
    public void NonSensitive_header_values_are_preserved()
    {
        var headers = new Dictionary<string, string?>
        {
            ["Content-Type"] = "application/json",
            ["X-Correlation-Id"] = "abc-123",
        };

        MeInvoiceLogScrubber.Redact(headers);

        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("abc-123", headers["X-Correlation-Id"]);
    }

    [Fact]
    public void Body_password_field_is_masked()
    {
        const string body = "{\"username\":\"alice\",\"password\":\"hunter2\",\"appId\":\"abc\"}";

        var redacted = MeInvoiceLogScrubber.Redact(body);

        Assert.DoesNotContain("hunter2", redacted);
        Assert.Contains("alice", redacted);
    }

    [Fact]
    public void Body_appId_field_is_masked()
    {
        const string body = "{\"AppId\":\"super-secret-app-id\"}";

        var redacted = MeInvoiceLogScrubber.Redact(body);

        Assert.DoesNotContain("super-secret-app-id", redacted);
    }
}
