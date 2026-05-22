using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Layering;

/// <summary>
/// Enforces the constitution layer rules at the assembly level. Domain has
/// zero outbound refs (System.* only); Application refs only Domain +
/// Microsoft.Extensions.Logging.Abstractions; Infrastructure is internal
/// except for the documented public types.
/// </summary>
public class MisaConnectESignLayerAuditTests
{
    [Fact]
    public void Domain_assembly_has_zero_external_references()
    {
        var domainAssembly = typeof(ESignException).Assembly;
        var refs = domainAssembly.GetReferencedAssemblies();
        foreach (var r in refs)
        {
            var name = r.Name ?? string.Empty;
            Assert.True(
                IsSystemAssembly(name),
                $"Domain references non-System assembly '{name}'. Domain must have zero external dependencies.");
        }
    }

    [Fact]
    public void Application_assembly_references_only_domain_and_logging_abstractions()
    {
        var appAssembly = typeof(IMisaESignWireClient).Assembly;
        var refs = appAssembly.GetReferencedAssemblies();
        foreach (var r in refs)
        {
            var name = r.Name ?? string.Empty;
            if (IsSystemAssembly(name)) continue;
            if (name == "MisaConnect.ESign.Domain") continue;
            if (name == "Microsoft.Extensions.Logging.Abstractions") continue;
            Assert.Fail($"Application references disallowed assembly '{name}'.");
        }
    }

    [Fact]
    public void Infrastructure_public_surface_is_only_documented_types()
    {
        var infraAssembly = typeof(MisaESignOptions).Assembly;
        var publicTypes = infraAssembly.GetExportedTypes();
        foreach (var t in publicTypes)
        {
            Assert.True(
                IsAllowedPublicInfrastructureType(t),
                $"Infrastructure exposes public type '{t.FullName}' which is not in the allow-list.");
        }
    }

    private static bool IsAllowedPublicInfrastructureType(Type t)
    {
        // DI extensions are intentionally internal — Client wraps them.
        // The documented public surface from Infrastructure is the Options
        // hierarchy plus the SystemClock and ESignLogScrubber, both of which
        // ship as defaults the consumer may instantiate directly in tests.
        var fullName = t.FullName ?? string.Empty;
        return fullName.StartsWith("MisaConnect.ESign.Infrastructure.Configuration.", StringComparison.Ordinal)
            || fullName == "MisaConnect.ESign.Infrastructure.Time.SystemClock"
            || fullName == "MisaConnect.ESign.Infrastructure.Logging.ESignLogScrubber";
    }

    [Fact]
    public void Slice2_otp_types_live_at_expected_layers()
    {
        Assert.Equal("MisaConnect.ESign.Domain", typeof(OtpDeliveryChannel).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(InvalidOtpException).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(ExpiredOtpException).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(ExhaustedOtpAttemptsException).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(OtpRejectedException).Assembly.GetName().Name);

        Assert.Equal("MisaConnect.ESign.Application", typeof(IOtpProvider).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(OtpChallenge).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(OtpSubmission).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(OtpResendResult).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(ExchangeOtp).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(ResendOtp).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(OtpErrorMapper).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(OtpSubmissionValidator).Assembly.GetName().Name);
    }

    [Fact]
    public void Slice3_per_format_types_live_at_expected_layers()
    {
        Assert.Equal("MisaConnect.ESign.Domain", typeof(DocumentFormat).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(XmlSignatureContext).Assembly.GetName().Name);

        Assert.Equal("MisaConnect.ESign.Application", typeof(XmlHashOutput).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(WordExcelHashOutput).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignHashInput).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(HashXmlDocument).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(HashWordDocument).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(HashExcelDocument).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(AttachSignatureToXml).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(AttachSignatureToWordExcel).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignXml).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignWord).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignExcel).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignXmlRequestValidator).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignWordRequestValidator).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SignExcelRequestValidator).Assembly.GetName().Name);
    }

    private static bool IsSystemAssembly(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal)
            || name == "System"
            || name == "mscorlib"
            || name == "netstandard"
            || name == "System.Runtime";
}
