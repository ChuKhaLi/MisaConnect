using System.Net;
using System.Text.Json;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Infrastructure.ESign;
using MisaConnect.ESign.Infrastructure.ESign.Mapping;
using MisaConnect.ESign.Infrastructure.ESign.Wire;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Errors;

// Slice 007 US3: MISA's validationFailures are surfaced in the exception detail only
// when IncludeRawErrorMessage is set; synthesized error codes/categories are unchanged
// (failures never feed the code-synthesis path). Contracts C4/C5/C6, FR-006/007.
public class ValidationFailuresDetailTests
{
    // Mirrors the captured MISA 400 (bug-report.md): generic errorCode + per-property failures.
    private const string Body =
        "{\"error\":null,\"errorCode\":\"e400\",\"devMsg\":\"dev\",\"userMsg\":\"user\"," +
        "\"validationFailures\":[" +
        "{\"property\":\"XmlDocs\",\"failureReason\":\"Phải có ít nhất 1 tài liệu\"}," +
        "{\"property\":\"WordDocs\",\"failureReason\":\"Phải có ít nhất 1 tài liệu\"}]}";

    private static (ResponseError envelope, string? rendered) Parse(string body)
    {
        var dto = JsonSerializer.Deserialize<ResponseErrorDto>(body, ESignJsonOptions.Wire)!;
        return (ResponseErrorMapper.ToDomain(dto), ResponseErrorMapper.RenderValidationFailures(dto.ValidationFailures));
    }

    private static ESignException Map(ResponseError envelope, string? vf, bool includeRaw) =>
        ESignErrorMapper.Map(
            ESignErrorMapper.EndpointHash, 400, envelope, "cid",
            includeRawErrorMessage: includeRaw, transactionId: null, attemptCount: null,
            lastStatusCode: HttpStatusCode.BadRequest, userName: null,
            requestedFormat: DocumentFormat.Pdf, validationFailuresDetail: vf);

    [Fact]
    public void Deserializes_and_renders_validationFailures()
    {
        var dto = JsonSerializer.Deserialize<ResponseErrorDto>(Body, ESignJsonOptions.Wire)!;
        Assert.NotNull(dto.ValidationFailures);
        Assert.Equal(2, dto.ValidationFailures!.Count);

        var rendered = ResponseErrorMapper.RenderValidationFailures(dto.ValidationFailures)!;
        Assert.Contains("[XmlDocs] Phải có ít nhất 1 tài liệu", rendered);
        Assert.Contains("[WordDocs] Phải có ít nhất 1 tài liệu", rendered);
    }

    [Fact]
    public void Detail_includes_failures_only_when_opt_in()
    {
        var (envelope, vf) = Parse(Body);

        var on = Map(envelope, vf, includeRaw: true);
        Assert.Contains("XmlDocs", on.Detail);
        Assert.Contains("WordDocs", on.Detail);

        var off = Map(envelope, vf, includeRaw: false);
        Assert.DoesNotContain("XmlDocs", off.Detail);
        Assert.DoesNotContain("validationFailures", off.Detail);
    }

    [Fact]
    public void Error_code_and_category_unchanged_by_failures()
    {
        var (envelope, vf) = Parse(Body);

        var withVf = Map(envelope, vf, includeRaw: true);
        // Public overload (no failures channel) — the synthesized code must match.
        var baseline = ESignErrorMapper.Map(ESignErrorMapper.EndpointHash, 400, envelope, "cid", includeRawErrorMessage: true);

        var a = Assert.IsType<ESignGeneralException>(withVf);
        var b = Assert.IsType<ESignGeneralException>(baseline);
        Assert.Equal(ESignErrorCategory.HashRejected, a.Category);
        Assert.Equal(b.Category, a.Category);
        Assert.Equal("e400", a.RawCode);
        Assert.Equal(b.RawCode, a.RawCode);
    }

    [Fact]
    public void Error_null_body_parses_and_surfaces_errorCode_not_none()
    {
        // Regression: MISA's 400 sends "error":null. A non-nullable bool used to
        // throw, the body was swallowed, and the error surfaced as <none>.
        var dto = JsonSerializer.Deserialize<ResponseErrorDto>(Body, ESignJsonOptions.Wire);
        Assert.NotNull(dto);
        Assert.Equal("e400", dto!.ErrorCode);

        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointHash, 400, ResponseErrorMapper.ToDomain(dto), "cid");
        Assert.Equal("e400", ex.RawCode);
        Assert.DoesNotContain("<none>", ex.Detail);
    }

    [Fact]
    public void Absent_validationFailures_renders_null()
    {
        var dto = JsonSerializer.Deserialize<ResponseErrorDto>("{\"errorCode\":\"e400\"}", ESignJsonOptions.Wire)!;
        Assert.Null(ResponseErrorMapper.RenderValidationFailures(dto.ValidationFailures));
    }

    [Fact]
    public void Partial_entries_render_defensively_without_throwing()
    {
        var dto = JsonSerializer.Deserialize<ResponseErrorDto>(
            "{\"validationFailures\":[" +
            "{\"property\":null,\"failureReason\":\"reason-only\"}," +
            "{\"property\":\"PropOnly\",\"failureReason\":null}," +
            "{\"property\":null,\"failureReason\":null}]}",
            ESignJsonOptions.Wire)!;

        var rendered = ResponseErrorMapper.RenderValidationFailures(dto.ValidationFailures);
        Assert.NotNull(rendered);
        Assert.Contains("reason-only", rendered);
        Assert.Contains("[PropOnly]", rendered);
    }
}
