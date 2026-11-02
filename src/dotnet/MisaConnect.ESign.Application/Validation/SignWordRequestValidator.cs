using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.Validation;

public sealed class SignWordRequestValidator
{
    private readonly ICorrelationIdAccessor _correlation;

    public SignWordRequestValidator(ICorrelationIdAccessor correlation)
    {
        _correlation = correlation;
    }

    public void Validate(SignWordWorkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        if (request.Word is null || request.Word.Length == 0)
        {
            errors.Add("Word bytes must be non-empty.");
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

        var sigInfo = request.SignatureInfo;
        if (sigInfo is null)
        {
            errors.Add("SignatureInfo is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sigInfo.SignatureName))
            {
                errors.Add("SignatureInfo.SignatureName is required.");
            }
            if (string.IsNullOrWhiteSpace(sigInfo.LogoImage))
            {
                errors.Add("SignatureInfo.LogoImage is required.");
            }
            if (sigInfo.SignatureDescription is null)
            {
                errors.Add("SignatureInfo.SignatureDescription is required.");
            }
            else
            {
                var d = sigInfo.SignatureDescription;
                if (string.IsNullOrWhiteSpace(d.SignedBy)) errors.Add("SignatureDescription.SignedBy is required.");
                if (string.IsNullOrWhiteSpace(d.Location)) errors.Add("SignatureDescription.Location is required.");
                if (string.IsNullOrWhiteSpace(d.Reason)) errors.Add("SignatureDescription.Reason is required.");
                if (string.IsNullOrWhiteSpace(d.Contact)) errors.Add("SignatureDescription.Contact is required.");
            }
            if (sigInfo.RenderingMode is < 0 or > 2)
            {
                errors.Add("SignatureInfo.RenderingMode must be 0, 1, or 2.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.Validation,
                "InvalidSignWordRequest",
                "SignWordRequest failed validation: " + string.Join("; ", errors),
                _correlation.Current,
                format: DocumentFormat.Word);
        }
    }
}
