namespace MisaConnect.ESign.Domain.Documents;

/// <summary>
/// Closed enum identifying which MISA-supported document format a given
/// request, result, or surfaced exception relates to. Used as the
/// <see cref="MisaConnect.ESign.Domain.Errors.ESignException.Format"/>
/// discriminator so consumers can branch on <c>(ExceptionType, Format)</c>
/// without string-matching MISA's <c>devMsg</c>.
/// </summary>
public enum DocumentFormat : byte
{
    /// <summary>
    /// Default sentinel. Reserved for failures surfaced outside any specific
    /// facade call (background refresh failures, DI-time validation, error
    /// paths reached before a format-specific facade was invoked).
    /// </summary>
    Unknown = 0,

    /// <summary>Consumer called <c>SignPdfAsync</c>.</summary>
    Pdf = 1,

    /// <summary>Consumer called <c>SignXmlAsync</c> (either overload).</summary>
    Xml = 2,

    /// <summary>Consumer called <c>SignWordAsync</c>.</summary>
    Word = 3,

    /// <summary>Consumer called <c>SignExcelAsync</c>.</summary>
    Excel = 4,
}
