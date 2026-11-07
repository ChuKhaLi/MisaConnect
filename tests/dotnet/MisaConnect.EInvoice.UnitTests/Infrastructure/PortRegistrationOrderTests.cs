using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.Templates;
using MisaConnect.EInvoice.Application.Validation;
using MisaConnect.EInvoice.Domain.Errors;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.Caching;
using MisaConnect.EInvoice.Infrastructure.Configuration;
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Xunit;

namespace MisaConnect.EInvoice.UnitTests.Infrastructure;

/// <summary>
/// EInvoice registers its default ports with <c>TryAdd…</c>: a consumer registration wins
/// whether it is made before or after <see cref="ServiceCollectionExtensions.AddMisaConnectEInvoice(IServiceCollection, Action{MisaEInvoiceOptions})"/>.
/// </summary>
public class PortRegistrationOrderTests
{
    private sealed record Port(Type Service, Func<object> Double, Type Default, bool Scoped);

    private static readonly Dictionary<string, Port> Ports = new()
    {
        ["ISystemClock"] = new(typeof(ISystemClock), () => new FakeClock(), typeof(SystemClock), false),
        ["IRefIdGenerator"] = new(typeof(IRefIdGenerator), () => new FakeRefIds(), typeof(GuidRefIdGenerator), false),
        ["ITokenCache"] = new(typeof(ITokenCache), () => new FakeTokenCache(), typeof(InMemoryTokenCache), false),
        ["IInvoiceValidator"] = new(typeof(IInvoiceValidator), () => new FakeValidator(), typeof(InvoiceValidator), false),
        ["ITemplateResolver"] = new(typeof(ITemplateResolver), () => new FakeTemplateResolver(), typeof(TemplateResolver), true),
        ["IDeleteOptionsAccessor"] = new(typeof(IDeleteOptionsAccessor), () => new FakeDeleteOptions(), typeof(DeleteOptionsAccessor), false),
    };

    public static TheoryData<string> PortNames => [.. Ports.Keys];

    [Theory]
    [MemberData(nameof(PortNames))]
    public void Registration_before_AddMisaConnectEInvoice_wins(string name)
    {
        var port = Ports[name];
        var services = BaseServices();
        var mine = port.Double();
        Register(services, port, mine);
        services.AddMisaConnectEInvoice(Configure);

        Assert.Same(mine, Resolve(services, port.Service));
    }

    [Theory]
    [MemberData(nameof(PortNames))]
    public void Registration_after_AddMisaConnectEInvoice_wins(string name)
    {
        var port = Ports[name];
        var services = BaseServices();
        services.AddMisaConnectEInvoice(Configure);
        var mine = port.Double();
        Register(services, port, mine);

        Assert.Same(mine, Resolve(services, port.Service));
    }

    [Theory]
    [MemberData(nameof(PortNames))]
    public void Default_is_resolved_without_override(string name)
    {
        var port = Ports[name];
        var services = BaseServices();
        services.AddMisaConnectEInvoice(Configure);

        Assert.IsType(port.Default, Resolve(services, port.Service));
    }

    [Theory]
    [MemberData(nameof(PortNames))]
    public void Calling_AddMisaConnectEInvoice_twice_registers_one_default(string name)
    {
        var port = Ports[name];
        var services = BaseServices();
        services.AddMisaConnectEInvoice(Configure);
        services.AddMisaConnectEInvoice(Configure);

        Assert.Single(services, d => d.ServiceType == port.Service);
    }

    [Fact]
    public void IConfiguration_overload_honours_registration_before()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Misa:EInvoice:Environment"] = "Sandbox",
                ["Misa:EInvoice:BaseUrl"] = "https://testapi.meinvoice.vn/api/integration",
                ["Misa:EInvoice:TaxCode"] = "0000000000",
                ["Misa:EInvoice:UserName"] = "test@example.com",
                ["Misa:EInvoice:Password"] = "secret",
                ["Misa:EInvoice:AppId"] = "001",
            })
            .Build();
        var services = BaseServices();
        var mine = new FakeTokenCache();
        services.AddSingleton<ITokenCache>(mine);
        services.AddMisaConnectEInvoice(configuration);

        Assert.Same(mine, Resolve(services, typeof(ITokenCache)));
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The Infrastructure layer alone does not register a correlation accessor (Client does).
        services.AddSingleton<ICorrelationIdAccessor>(new FakeCorrelation());
        return services;
    }

    private static void Configure(MisaEInvoiceOptions o)
    {
        o.Environment = MeInvoiceEnvironment.Sandbox;
        o.BaseUrl = "https://testapi.meinvoice.vn/api/integration";
        o.TaxCode = "0000000000";
        o.UserName = "test@example.com";
        o.Password = "secret";
        o.AppId = "001";
    }

    private static void Register(IServiceCollection services, Port port, object instance)
    {
        if (port.Scoped)
        {
            services.AddScoped(port.Service, _ => instance);
        }
        else
        {
            services.AddSingleton(port.Service, instance);
        }
    }

    private static object Resolve(IServiceCollection services, Type service)
    {
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService(service);
    }

    private sealed class FakeCorrelation : ICorrelationIdAccessor
    {
        public string CorrelationId => "test";
    }

    private sealed class FakeClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class FakeRefIds : IRefIdGenerator
    {
        public RefId NewRefId() => RefId.NewGuid();
    }

    private sealed class FakeTokenCache : ITokenCache
    {
        public Task<AccessToken?> TryGetAsync(string key, CancellationToken ct) => Task.FromResult<AccessToken?>(null);
        public Task SetAsync(string key, AccessToken value, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeValidator : IInvoiceValidator
    {
        public IReadOnlyList<ValidationFailure> Validate(Invoice invoice) => [];
        public IReadOnlyList<ValidationFailure> ValidateBatch(BatchSubmission batch) => [];
    }

    private sealed class FakeTemplateResolver : ITemplateResolver
    {
        public Task<TemplateResolution> ResolveAsync(TemplateRef? callerSupplied, bool invoiceWithCode, CancellationToken ct) =>
            Task.FromResult<TemplateResolution>(new TemplateResolution.NoActiveTemplate());
    }

    private sealed class FakeDeleteOptions : IDeleteOptionsAccessor
    {
        public bool IncludeRawErrorMessage => false;
    }
}
