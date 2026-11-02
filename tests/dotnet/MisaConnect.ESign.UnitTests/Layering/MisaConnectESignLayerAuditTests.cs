using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Application.Sessions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Application.Webhook;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using MisaConnect.ESign.Domain.Webhook;
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

    [Fact]
    public void Slice4_webhook_types_live_at_expected_layers()
    {
        // Domain.Webhook.* are pure value/enum types
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookEnvelope).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookSignature).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookStatus).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookAck).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookOutcome).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(BeginResult).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookValidationCategory).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(WebhookValidationException).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Domain", typeof(MalformedEnvelopeException).Assembly.GetName().Name);

        // Application.Sessions.* live in Application (not Domain) because they wrap Application-layer hash output types
        Assert.Equal("MisaConnect.ESign.Application", typeof(SigningSession).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(PerFormatHashPayload).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(SigningSessionCachedSuccess).Assembly.GetName().Name);

        // Application.Webhook ports + use cases
        Assert.Equal("MisaConnect.ESign.Application", typeof(IWebhookDeliveryHook).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(WebhookHandleResult).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(ISigningSessionStore).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(IFinalizeLockOwner).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(WebhookEnvelopeValidator).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(BeginSignPdf).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(BeginSignXml).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(BeginSignWord).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(BeginSignExcel).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(HandleWebhook).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Application", typeof(FinalizeFromWebhook).Assembly.GetName().Name);

        // Infrastructure.Sessions.InMemorySigningSessionStore + Infrastructure.ESign.Webhook.* stay internal
        var infraAssembly = typeof(MisaESignOptions).Assembly;
        Assert.DoesNotContain(infraAssembly.GetExportedTypes(),
            t => t.FullName == "MisaConnect.ESign.Infrastructure.Sessions.InMemorySigningSessionStore");
        Assert.DoesNotContain(infraAssembly.GetExportedTypes(),
            t => (t.FullName ?? "").StartsWith("MisaConnect.ESign.Infrastructure.ESign.Webhook.", StringComparison.Ordinal));

        // Webhook configuration types ARE public (DI-touching)
        Assert.Equal("MisaConnect.ESign.Infrastructure", typeof(MisaESignWebhookOptions).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Infrastructure", typeof(MisaESignWebhookSessionOptions).Assembly.GetName().Name);
        Assert.Equal("MisaConnect.ESign.Infrastructure", typeof(WebhookMode).Assembly.GetName().Name);
    }

    private static bool IsSystemAssembly(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal)
            || name == "System"
            || name == "mscorlib"
            || name == "netstandard"
            || name == "System.Runtime";
}
