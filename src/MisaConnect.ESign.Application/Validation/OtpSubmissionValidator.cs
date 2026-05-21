using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Domain.Errors;

namespace MisaConnect.ESign.Application.Validation;

public sealed class OtpSubmissionValidator
{
    private readonly ICorrelationIdAccessor _correlation;

    public OtpSubmissionValidator(ICorrelationIdAccessor correlation)
    {
        _correlation = correlation;
    }

    public void Validate(OtpSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (string.IsNullOrWhiteSpace(submission.Code))
        {
            throw new ESignGeneralException(
                ESignErrorCategory.Validation,
                "InvalidOtpSubmission",
                "OtpSubmission.Code must be non-empty.",
                _correlation.Current);
        }
    }
}
