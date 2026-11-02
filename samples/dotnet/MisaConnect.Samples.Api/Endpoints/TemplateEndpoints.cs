using MisaConnect.EInvoice.Application.UseCases;

namespace MisaConnect.Samples.Api.Endpoints;

public static class TemplateEndpoints
{
    public static IEndpointRouteBuilder MapTemplateEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/api/templates", async (
            ListActiveTemplates useCase,
            bool withCode = true,
            CancellationToken ct = default) =>
        {
            var templates = await useCase.ExecuteAsync(withCode, ct).ConfigureAwait(false);
            var dtos = new List<object>(templates.Count);
            foreach (var t in templates)
            {
                dtos.Add(new
                {
                    ipTemplateId = t.IPTemplateID,
                    invSeries = t.InvSeries,
                    templateName = t.TemplateName,
                    invTemplateNo = t.InvTemplateNo,
                    templateType = t.TemplateType,
                    usesTaxAuthorityCode = t.UsesTaxAuthorityCode,
                    isActive = t.IsActive,
                    isMoreVatRate = t.IsMoreVATRate,
                });
            }
            return Results.Ok(new { templates = dtos });
        });

        return builder;
    }
}
