using MisaConnect.EInvoice.Domain.Errors;

namespace MisaConnect.EInvoice.Application.Errors;

/// <summary>
/// FR-012 mapping from raw MISA <c>errorCode</c> values to the structured
/// <see cref="MeInvoiceErrorCategory"/> taxonomy. Unknown codes surface as
/// <see cref="MeInvoiceErrorCategory.MisaUnknown"/> with the raw code attached
/// — never silently swallowed.
/// </summary>
internal static class MeInvoiceErrorMapper
{
    internal static readonly IReadOnlyDictionary<string, MeInvoiceErrorCategory> ExactMap =
        new Dictionary<string, MeInvoiceErrorCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Configuration
            ["InvalidAppID"] = MeInvoiceErrorCategory.Configuration,
            ["InActiveAppID"] = MeInvoiceErrorCategory.Configuration,
            ["InvalidTaxCode"] = MeInvoiceErrorCategory.Configuration,
            ["LicenseInfo_NotBuy"] = MeInvoiceErrorCategory.Configuration,
            ["LicenseInfo_OutOfInvoice"] = MeInvoiceErrorCategory.Configuration,
            ["LicenseInfo_Expired"] = MeInvoiceErrorCategory.Configuration,

            // Authentication / token
            ["UnAuthorize"] = MeInvoiceErrorCategory.Authentication,
            ["TokenExpiredCode"] = MeInvoiceErrorCategory.Authentication,
            ["InvalidTokenCode"] = MeInvoiceErrorCategory.Authentication,

            // Template state
            ["DuplicateTemplateName"] = MeInvoiceErrorCategory.TemplateState,
            ["DuplicateTemplateNo"] = MeInvoiceErrorCategory.TemplateState,
            ["InvoiceTemplateNotExist"] = MeInvoiceErrorCategory.TemplateState,
            ["TemplateIsNotUsing"] = MeInvoiceErrorCategory.TemplateState,
            ["NoActiveTemplate"] = MeInvoiceErrorCategory.TemplateState,
            ["UnknownTemplate"] = MeInvoiceErrorCategory.TemplateState,
            ["AmbiguousTemplate"] = MeInvoiceErrorCategory.Configuration,

            // Declaration state
            ["DeclarationNotExist"] = MeInvoiceErrorCategory.DeclarationState,
            ["InvalidDeclaration"] = MeInvoiceErrorCategory.DeclarationState,
            ["ExistDeclarationNotReceive"] = MeInvoiceErrorCategory.DeclarationState,
            ["ExistsInvoiceNextYear"] = MeInvoiceErrorCategory.DeclarationState,
            ["InvoiceTemplateNotValidInDeclaration"] = MeInvoiceErrorCategory.DeclarationState,

            // Payload validation
            ["InvalidXMLData"] = MeInvoiceErrorCategory.Validation,
            ["TaxRateInfo_VATRateName"] = MeInvoiceErrorCategory.Validation,
            ["BuyerTaxCode_TaxCodeInvalid"] = MeInvoiceErrorCategory.Validation,
            ["InvalidInvoiceDate"] = MeInvoiceErrorCategory.Validation,
            ["TaxReductionDateInValid"] = MeInvoiceErrorCategory.Validation,
            ["InvalidXMLContainEmoji"] = MeInvoiceErrorCategory.Validation,
            ["CreateInvoiceDataError"] = MeInvoiceErrorCategory.Validation,
            ["InvalidInvoiceData"] = MeInvoiceErrorCategory.Validation,

            // Batch / size — surfaced as Validation in slice 1
            ["InvoiceQuantityTooLarge"] = MeInvoiceErrorCategory.Validation,
            ["XMLTooLong"] = MeInvoiceErrorCategory.Validation,

            // Duplicate / uniqueness
            ["DuplicateInvoiceRefID"] = MeInvoiceErrorCategory.DuplicateOrUniqueness,
            ["DuplicateTransactionID"] = MeInvoiceErrorCategory.DuplicateOrUniqueness,
            ["InvoiceDuplicated"] = MeInvoiceErrorCategory.DuplicateOrUniqueness,
            ["InvoiceNumberNotCotinuous"] = MeInvoiceErrorCategory.DuplicateOrUniqueness,

            // Resource not found
            ["InvalidTransactionID"] = MeInvoiceErrorCategory.ResourceNotFound,
            ["RefIdNotFound"] = MeInvoiceErrorCategory.ResourceNotFound,

            // Signing (out of slice, acknowledged)
            ["SignatureEmpty"] = MeInvoiceErrorCategory.Signing,
            ["InvalidSignature"] = MeInvoiceErrorCategory.Signing,
            ["CertRevocation"] = MeInvoiceErrorCategory.Signing,
            ["InvalidCertByRegistration"] = MeInvoiceErrorCategory.Signing,
            ["HasRegistrationStopUseCert"] = MeInvoiceErrorCategory.Signing,
            ["SigningTimeNotInRegistration"] = MeInvoiceErrorCategory.Signing,
            ["SignSoftDream78Exception"] = MeInvoiceErrorCategory.Signing,
            ["SignEsignHSMError"] = MeInvoiceErrorCategory.Signing,
            ["CallSignServiceFail"] = MeInvoiceErrorCategory.Signing,
            ["X509SubjectName"] = MeInvoiceErrorCategory.Signing,
            ["X509Certificate"] = MeInvoiceErrorCategory.Signing,

            // Replacement / adjustment (slice 6)
            ["InvoiceCannotReplace"] = MeInvoiceErrorCategory.Replacement,
            ["InvoiceCannotAdjust"] = MeInvoiceErrorCategory.Replacement,
            ["InvoiceCannotReplaceByStatusNew"] = MeInvoiceErrorCategory.Replacement,
            ["HasAdjustmentInvoice"] = MeInvoiceErrorCategory.Replacement,

            // Other workflows
            ["InvalidInvNo"] = MeInvoiceErrorCategory.OtherWorkflow,

            // MISA catch-all
            ["Exception"] = MeInvoiceErrorCategory.MisaUnavailable,
        };

    internal static readonly IReadOnlyList<(string Prefix, MeInvoiceErrorCategory Category)> PrefixMap = new[]
    {
        ("InvoiceDetail_", MeInvoiceErrorCategory.Validation),
        ("RequireError_", MeInvoiceErrorCategory.Validation),
        ("StockInTaxCode_NotInfo_", MeInvoiceErrorCategory.OtherWorkflow),
    };

    public static MeInvoiceErrorCode Map(string? rawCode, string? message)
    {
        if (string.IsNullOrEmpty(rawCode))
        {
            return new MeInvoiceErrorCode(MeInvoiceErrorCategory.MisaUnknown, rawCode ?? string.Empty, message);
        }

        if (ExactMap.TryGetValue(rawCode, out var exact))
        {
            return new MeInvoiceErrorCode(exact, rawCode, message);
        }

        foreach (var (prefix, category) in PrefixMap)
        {
            if (rawCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = rawCode.Substring(prefix.Length);
                string? field = null;
                string? component = null;
                var parts = tail.Split('_');
                if (parts.Length >= 1 && parts[0].Length > 0) field = parts[0];
                if (parts.Length >= 2 && parts[1].Length > 0) component = parts[1];
                return new MeInvoiceErrorCode(category, rawCode, message, field, component);
            }
        }

        return new MeInvoiceErrorCode(MeInvoiceErrorCategory.MisaUnknown, rawCode, message);
    }
}
