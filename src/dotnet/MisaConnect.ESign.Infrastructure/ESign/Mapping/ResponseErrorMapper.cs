using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.ESign.Wire;

namespace MisaConnect.ESign.Infrastructure.ESign.Mapping;

internal static class ResponseErrorMapper
{
    public static ResponseError ToDomain(ResponseErrorDto dto) =>
        new(Error: dto.Error ?? false, ErrorCode: dto.ErrorCode, DevMsg: dto.DevMsg, UserMsg: dto.UserMsg);

    public static ResponseError FromLoginStatus(LoginStatusBlockDto dto) =>
        new(Error: dto.Error, ErrorCode: dto.ErrorCode, DevMsg: dto.DevMsg, UserMsg: dto.UserMsg);

    /// <summary>
    /// Slice 007: renders MISA's <c>validationFailures</c> to a single sanitized
    /// string for the error detail (gated downstream by IncludeRawErrorMessage).
    /// Contains only MISA field names + reason strings — no PII/tokens. Returns
    /// <c>null</c> when there is nothing to surface.
    /// </summary>
    public static string? RenderValidationFailures(IReadOnlyList<ValidationFailureDto>? failures)
    {
        if (failures is null || failures.Count == 0) return null;
        var parts = new List<string>(failures.Count);
        foreach (var f in failures)
        {
            var hasProp = !string.IsNullOrWhiteSpace(f.Property);
            var hasReason = !string.IsNullOrWhiteSpace(f.FailureReason);
            if (!hasProp && !hasReason) continue;
            if (!hasProp) parts.Add(f.FailureReason!.Trim());
            else if (!hasReason) parts.Add($"[{f.Property}]");
            else parts.Add($"[{f.Property}] {f.FailureReason}");
        }
        return parts.Count == 0 ? null : "validationFailures: " + string.Join("; ", parts);
    }
}
