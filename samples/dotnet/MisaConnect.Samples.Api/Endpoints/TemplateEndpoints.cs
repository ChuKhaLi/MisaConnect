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

        builder.MapGet("/api/templates/ticket", async (
            ListTicketTemplates useCase,
            bool withCode = true,
            int start = 0,
            int length = 20,
            bool fromCashRegister = false,
            CancellationToken ct = default) =>
        {
            IReadOnlyList<MisaConnect.EInvoice.Domain.Templates.TicketTemplate> rows;
            try
            {
                rows = await useCase
                    .ExecuteAsync(new TicketTemplateListRequest(withCode, start, length, fromCashRegister), ct)
                    .ConfigureAwait(false);
            }
            catch (MisaConnect.EInvoice.Domain.Errors.MeInvoiceException ex)
            {
                return ErrorMapping.ToResult(ex);
            }

            var dtos = new List<object>(rows.Count);
            foreach (var t in rows)
            {
                dtos.Add(new
                {
                    ticketTemplateId = t.TicketTemplateID,
                    templateName = t.TemplateName,
                    invSeries = t.InvSeries,
                    isActive = t.IsActive,
                    ticketType = t.TicketType,
                    templateType = t.TemplateType,
                    serviceName = t.ServiceName,
                    isInheritFromOldTemplate = t.IsInheritFromOldTemplate,
                    isTicketCode = t.IsTicketCode,
                });
            }
            return Results.Ok(new { ticketTemplates = dtos });
        });

        return builder;
    }
}
