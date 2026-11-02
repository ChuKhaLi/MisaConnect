using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.Validation;

public sealed class SignXmlRequestValidator
{
    private readonly ICorrelationIdAccessor _correlation;

    public SignXmlRequestValidator(ICorrelationIdAccessor correlation)
    {
        _correlation = correlation;
    }

    public void Validate(SignXmlWorkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.Xml))
        {
            errors.Add("Xml content must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentName))
        {
            errors.Add("DocumentName is required.");
        }
        else if (request.DocumentName.Length > 100)
        {
            errors.Add("DocumentName must be <= 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            errors.Add("DocumentId is required.");
        }
        else if (request.DocumentId.Length > 36)
        {
            errors.Add("DocumentId must be <= 36 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.DataToBeDisplayed))
        {
            errors.Add("DataToBeDisplayed is required.");
        }

        var ctx = request.SignatureContext;
        if (ctx is null)
        {
            errors.Add("SignatureContext is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ctx.SignatureName))
            {
                errors.Add("SignatureContext.SignatureName is required.");
            }
            if (ctx.SignatureDescription is null)
            {
                errors.Add("SignatureContext.SignatureDescription is required.");
            }
            else
            {
                var d = ctx.SignatureDescription;
                if (string.IsNullOrWhiteSpace(d.SignedBy)) errors.Add("SignatureDescription.SignedBy is required.");
                if (string.IsNullOrWhiteSpace(d.Location)) errors.Add("SignatureDescription.Location is required.");
                if (string.IsNullOrWhiteSpace(d.Reason)) errors.Add("SignatureDescription.Reason is required.");
                if (string.IsNullOrWhiteSpace(d.Contact)) errors.Add("SignatureDescription.Contact is required.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.Validation,
                "InvalidSignXmlRequest",
                "SignXmlRequest failed validation: " + string.Join("; ", errors),
                _correlation.Current,
                format: DocumentFormat.Xml);
        }
    }
}
