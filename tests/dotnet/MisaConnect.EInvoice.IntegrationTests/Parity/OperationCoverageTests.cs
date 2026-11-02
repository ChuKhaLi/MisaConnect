using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Application.Operations;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Client;
using MisaConnect.EInvoice.IntegrationTests.Api;
using Xunit;

namespace MisaConnect.EInvoice.IntegrationTests.Parity;

public class OperationCoverageTests
{
    [Fact]
    public async Task Every_operation_has_a_registered_HTTP_route()
    {
        await using var factory = new WebFactory();
        // Force host startup.
        _ = factory.CreateClient();

        var endpointSources = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var routePatterns = endpointSources
            .Select(e => $"{ExtractMethod(e)} {e.RoutePattern.RawText}")
            .Where(s => s.Contains("/api/", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeRoute)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var op in MeInvoiceOperations.All)
        {
            var expected = NormalizeRoute(op.HttpRoute);
            Assert.Contains(expected, routePatterns);
        }
    }

    [Fact]
    public void Every_operation_has_a_matching_IEInvoiceClient_method()
    {
        var methods = typeof(IMisaEInvoiceClient).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        foreach (var op in MeInvoiceOperations.All)
        {
            Assert.Contains(op.ClientMethodName, methods);
        }
    }

    [Fact]
    public void All_use_case_types_resolve_via_DI()
    {
        using var factory = new WebFactory();
        using var scope = factory.Services.CreateScope();
        foreach (var op in MeInvoiceOperations.All)
        {
            var instance = scope.ServiceProvider.GetService(op.UseCaseType);
            Assert.NotNull(instance);
        }
    }

    [Fact]
    public void Token_acquisition_is_not_in_the_user_facing_manifest()
    {
        Assert.DoesNotContain(MeInvoiceOperations.All, op => op.UseCaseType == typeof(EnsureAccessToken));
    }

    private static string ExtractMethod(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>();
        if (metadata is not null && metadata.HttpMethods.Count > 0)
        {
            return metadata.HttpMethods[0];
        }
        return "GET";
    }

    private static string NormalizeRoute(string route)
    {
        var parts = route.Split(' ', 2);
        if (parts.Length != 2) return route;
        var path = parts[1].TrimStart('/');
        return $"{parts[0].ToUpperInvariant()} /{path}";
    }
}
