using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.Validation;

public sealed class SignPdfRequestValidator
{
    private readonly ICorrelationIdAccessor _correlation;

    public SignPdfRequestValidator(ICorrelationIdAccessor correlation)
    {
        _correlation = correlation;
    }

    public void Validate(SignPdfWorkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        if (request.Pdf is null || request.Pdf.Bytes is null || request.Pdf.Bytes.Length == 0)
        {
            errors.Add("Pdf bytes must be non-empty.");
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
            if (sigInfo.Page is < 1)
            {
                errors.Add("SignatureInfo.Page must be >= 1 when specified.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ESignGeneralException(
                ESignErrorCategory.Validation,
                "InvalidSignPdfRequest",
                "SignPdfRequest failed validation: " + string.Join("; ", errors),
                _correlation.Current);
        }
    }
}
