using MisaConnect.ESign.Application.Abstractions;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Abstractions;

// Slice 008 US4 / FR-009: the positional-record auto-ToString() would emit ALL
// members in plaintext (ClientKey + Password). The override must redact the two
// secret-bearing members so structured logging / interpolation cannot leak them.
public class MisaCredentialsTests
{
    [Fact]
    public void ToString_redacts_clientkey_and_password_but_keeps_identifiers()
    {
        var creds = new MisaCredentials("cid-value", "SECRET_KEY", "user-value", "SECRET_PWD");

        var text = creds.ToString();

        Assert.DoesNotContain("SECRET_KEY", text);
        Assert.DoesNotContain("SECRET_PWD", text);
        Assert.Contains("<redacted>", text);
        Assert.Contains("user-value", text);
        Assert.Contains("cid-value", text);
    }

    [Fact]
    public void Interpolation_does_not_leak_secrets()
    {
        var creds = new MisaCredentials("cid", "SECRET_KEY", "user", "SECRET_PWD");

        var interpolated = $"creds={creds}";

        Assert.DoesNotContain("SECRET_KEY", interpolated);
        Assert.DoesNotContain("SECRET_PWD", interpolated);
    }
}
