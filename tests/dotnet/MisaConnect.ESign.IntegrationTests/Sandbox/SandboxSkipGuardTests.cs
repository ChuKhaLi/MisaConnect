using System.Reflection;
using MisaConnect.ESign.Domain.Errors;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

/// <summary>
/// Network-free guards for the sandbox skip plumbing. The CI step "Guard ESign
/// sandbox test discovery" additionally fails the build if the sandbox tests
/// stop being discovered.
/// </summary>
public class SandboxSkipGuardTests
{
    [Fact]
    public void SandboxFact_is_a_plain_fact_attribute()
    {
        Assert.True(typeof(FactAttribute).IsAssignableFrom(typeof(SandboxFactAttribute)));
        Assert.Null(typeof(SandboxFactAttribute).GetCustomAttribute<AttributeUsageAttribute>(inherit: false));
    }

    [Fact]
    public void No_active_certificate_becomes_a_skip_when_not_required()
    {
        var reason = SandboxSigning.SkipReasonFor(NoCert(), required: false);
        Assert.NotNull(reason);
        Assert.Contains(SandboxSigning.RequiredVar, reason);
    }

    [Fact]
    public void No_active_certificate_is_not_a_skip_when_required() =>
        Assert.Null(SandboxSigning.SkipReasonFor(NoCert(), required: true));

    [Fact]
    public void Other_failures_are_never_skip_reasons() =>
        Assert.Null(SandboxSigning.SkipReasonFor(new InvalidOperationException(), required: false));

    [Fact]
    public async Task No_active_certificate_fails_when_required()
    {
        await Assert.ThrowsAsync<NoActiveCertificateException>(() =>
            SandboxSigning.SignOrSkipAsync<int>(() => throw NoCert(), required: true));
    }

    [Fact]
    public async Task Other_failures_are_never_turned_into_skips()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SandboxSigning.SignOrSkipAsync<int>(() => throw new InvalidOperationException(), required: false));
    }

    private static NoActiveCertificateException NoCert() => new("no ACTIVE certificate", "test-correlation-id");
}
