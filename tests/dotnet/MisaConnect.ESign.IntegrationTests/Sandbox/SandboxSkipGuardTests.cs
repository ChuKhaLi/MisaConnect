using System.Reflection;
using MisaConnect.ESign.Domain.Errors;
using Xunit;
using Xunit.Sdk;

namespace MisaConnect.ESign.IntegrationTests.Sandbox;

/// <summary>
/// Network-free guards for the sandbox skip plumbing. A wrongly shaped
/// <see cref="SandboxFactAttribute"/> does not fail: every sandbox test just
/// disappears from discovery, so these checks pin the shape down.
/// </summary>
public class SandboxSkipGuardTests
{
    [Fact]
    public void SandboxFact_declares_the_skippable_fact_discoverer()
    {
        var discoverer = typeof(SandboxFactAttribute).GetCustomAttribute<XunitTestCaseDiscovererAttribute>(inherit: false);
        Assert.NotNull(discoverer);
    }

    [Fact]
    public void SandboxFact_has_only_a_parameterless_constructor()
    {
        // SkippableFactDiscoverer reads the first constructor argument as its
        // params Type[] list; any other argument breaks discovery.
        var ctors = typeof(SandboxFactAttribute).GetConstructors();
        Assert.All(ctors, c => Assert.Empty(c.GetParameters()));
    }

    [Fact]
    public async Task No_active_certificate_becomes_a_skip_when_not_required()
    {
        var ex = await Assert.ThrowsAsync<Xunit.SkipException>(() =>
            SandboxSigning.SignOrSkipAsync<int>(() => throw NoCert(), required: false));
        Assert.Contains(SandboxSigning.RequiredVar, ex.Message);
    }

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
