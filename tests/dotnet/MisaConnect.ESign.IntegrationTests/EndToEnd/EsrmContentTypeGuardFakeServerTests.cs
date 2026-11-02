using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

/// <summary>
/// US2 (Defect D): a 2xx response with a non-JSON body must fail loudly with a
/// clear, secret-free error — not silently collapse into "no active certificate".
/// A legitimately empty JSON array still surfaces as "no active certificate".
/// </summary>
public class EsrmContentTypeGuardFakeServerTests
{
    [Fact]
    public async Task Html_success_on_cert_list_throws_clear_error_naming_endpoint_and_content_type()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsReturnHtml200 = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<ESignGeneralException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Contains("Certificates/by-userId", ex.Message);
        Assert.Contains("text/html", ex.Message);
        // FR-007: no secrets in the error (the bearer must never leak).
        Assert.DoesNotContain("rs-token", ex.Message);
    }

    [Fact]
    public async Task Empty_json_array_on_cert_list_yields_no_active_certificate()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsReturnEmptyArray200 = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        await Assert.ThrowsAsync<NoActiveCertificateException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task Unparseable_json_cert_body_throws_clear_error_without_echoing_body()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsReturnJsonNonArray200 = true; // 200 application/json, not a cert array
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<ESignGeneralException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Contains("Certificates/by-userId", ex.Message);
        // The cert-endpoint body can carry PII (e.g. emailName); the error must NOT echo it.
        Assert.DoesNotContain("leak@example.test", ex.Message);
        Assert.DoesNotContain("rs-token", ex.Message);
    }

    [Fact]
    public async Task Plus_json_content_type_is_accepted_and_parses()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsContentTypeOverride = "application/vnd.api+json";
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        var result = await client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None);

        Assert.NotNull(result.SignedPdf); // +json is treated as JSON and parses
    }

    [Fact]
    public async Task Non_json_non_html_content_type_throws_clear_error()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsContentTypeOverride = "text/plain";
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<ESignGeneralException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Contains("Certificates/by-userId", ex.Message);
        Assert.Contains("text/plain", ex.Message);
    }

    [Fact]
    public async Task Missing_content_type_throws_clear_error()
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.CertsReturnNoContentType200 = true;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);

        using var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();

        var ex = await Assert.ThrowsAsync<ESignGeneralException>(
            () => client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));

        Assert.Contains("Certificates/by-userId", ex.Message);
    }
}
