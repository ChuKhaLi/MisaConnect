using MisaConnect.EInvoice.Application.Abstractions;
using MisaConnect.EInvoice.Application.UseCases;
using MisaConnect.EInvoice.Domain.Invoices;
using MisaConnect.EInvoice.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole());
services.AddMisaConnectEInvoice(config);
services.AddSingleton<ICorrelationIdAccessor>(_ => new StaticCorrelationIdAccessor());

using var provider = services.BuildServiceProvider();

await using var scope = provider.CreateAsyncScope();
var listTemplates = scope.ServiceProvider.GetRequiredService<ListActiveTemplates>();

Console.WriteLine("Listing active MISA eInvoice templates (invoiceWithCode = true)...");
var templates = await listTemplates.ExecuteAsync(invoiceWithCode: true, CancellationToken.None);
foreach (var template in templates)
{
    Console.WriteLine($"  {template.InvSeries} / {template.InvTemplateNo} - {template.TemplateName}");
}

internal sealed class StaticCorrelationIdAccessor : ICorrelationIdAccessor
{
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
}

public partial class Program;
