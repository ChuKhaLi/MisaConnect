using System.Net;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Domain.Errors;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Errors;

public class OtpErrorMapperTests
{
    // A.8.1 — canonical errorCode dispatch
    [Theory]
    [InlineData("122", typeof(AuthenticationFailedException), true)]
    [InlineData("1001", typeof(InvalidOtpException), false)]
    [InlineData("1002", typeof(ExpiredOtpException), false)]
    [InlineData("1003", typeof(ExhaustedOtpAttemptsException), false)]
    public void Canonical_codes_dispatch_to_typed_exception(string code, Type expected, bool requires2FA)
    {
        var envelope = new ResponseError(true, code, null, null);
        var ex = OtpErrorMapper.MapTwoFactor(400, envelope, "cid", userName: "alice");
        Assert.IsType(expected, ex);
        var auth = Assert.IsAssignableFrom<AuthenticationFailedException>(ex);
        Assert.Equal(requires2FA, auth.Requires2FA);
        if (requires2FA) Assert.Equal("alice", auth.Username);
    }

    // A.8.2 — substring keyword fallback
    [Theory]
    [InlineData("Invalid OTP", typeof(InvalidOtpException))]
    [InlineData("OTP wrong", typeof(InvalidOtpException))]
    [InlineData("Sai mã", typeof(InvalidOtpException))]
    [InlineData("OTP không hợp lệ", typeof(InvalidOtpException))]
    [InlineData("OTP expired", typeof(ExpiredOtpException))]
    [InlineData("OTP đã hết hạn", typeof(ExpiredOtpException))]
    [InlineData("Max attempts exceeded", typeof(ExhaustedOtpAttemptsException))]
    [InlineData("Vượt quá số lần", typeof(ExhaustedOtpAttemptsException))]
    public void Keyword_fallback_dispatches_when_canonical_misses(string devMsg, Type expected)
    {
        var envelope = new ResponseError(true, "9999", DevMsg: devMsg, UserMsg: null);
        var ex = OtpErrorMapper.MapTwoFactor(400, envelope, "cid");
        Assert.IsType(expected, ex);
    }

    [Fact]
    public void No_match_falls_through_to_residual_otp_rejected()
    {
        var envelope = new ResponseError(true, "9999", "totally unknown", "totally unknown");
        var ex = OtpErrorMapper.MapTwoFactor(400, envelope, "cid");
        Assert.IsType<OtpRejectedException>(ex);
        Assert.Equal("9999", ex.RawCode);
    }

    [Fact]
    public void Empty_envelope_falls_through_to_residual_otp_rejected()
    {
        var ex = OtpErrorMapper.MapTwoFactor(400, null, "cid");
        Assert.IsType<OtpRejectedException>(ex);
    }

    // A.8.4 — cross-cutting cases
    [Fact]
    public void Status_429_is_transport_failure()
    {
        var ex = OtpErrorMapper.MapTwoFactor(429, null, "cid", attemptCount: 4, lastStatusCode: (HttpStatusCode)429);
        Assert.IsType<ESignTransportException>(ex);
    }

    [Fact]
    public void Status_500_is_transport_failure()
    {
        var ex = OtpErrorMapper.MapTwoFactor(500, null, "cid", attemptCount: 3, lastStatusCode: HttpStatusCode.InternalServerError);
        var t = Assert.IsType<ESignTransportException>(ex);
        Assert.Equal(HttpStatusCode.InternalServerError, t.LastStatusCode);
    }

    [Fact]
    public void Status_401_surfaces_as_otp_rejection_not_refresh_loop()
    {
        var envelope = new ResponseError(true, "401", null, null);
        var ex = OtpErrorMapper.MapTwoFactor(401, envelope, "cid");
        Assert.IsType<OtpRejectedException>(ex);
    }

    // IncludeRawErrorMessage toggle
    [Fact]
    public void Default_detail_omits_raw_messages()
    {
        var envelope = new ResponseError(true, "1001", DevMsg: "secret-dev", UserMsg: "secret-user");
        var ex = OtpErrorMapper.MapTwoFactor(400, envelope, "cid", includeRawErrorMessage: false);
        Assert.DoesNotContain("secret-dev", ex.Detail);
        Assert.DoesNotContain("secret-user", ex.Detail);
    }

    [Fact]
    public void Opt_in_includes_raw_messages()
    {
        var envelope = new ResponseError(true, "1001", DevMsg: "show-dev", UserMsg: "show-user");
        var ex = OtpErrorMapper.MapTwoFactor(400, envelope, "cid", includeRawErrorMessage: true);
        Assert.Contains("show-dev", ex.Detail);
        Assert.Contains("show-user", ex.Detail);
    }

    // A.9.1 — resend permutations
    [Fact]
    public void Resend_200_ok_returns_success_result()
    {
        var result = OtpErrorMapper.MapResendResult(200, null, "cid");
        Assert.True(result.Success);
        Assert.Null(result.RawCode);
    }

    [Fact]
    public void Resend_200_with_typed_error_returns_failure_result()
    {
        var envelope = new ResponseError(true, "RateLimited", "too soon", "please wait");
        var result = OtpErrorMapper.MapResendResult(200, envelope, "cid");
        Assert.False(result.Success);
        Assert.Equal("RateLimited", result.RawCode);
        Assert.Equal("too soon", result.DevMsg);
        Assert.Equal("please wait", result.UserMsg);
    }

    [Fact]
    public void Resend_4xx_with_envelope_returns_failure_result()
    {
        var envelope = new ResponseError(true, "BadRequest", "bad", "u-bad");
        var result = OtpErrorMapper.MapResendResult(400, envelope, "cid");
        Assert.False(result.Success);
        Assert.Equal("BadRequest", result.RawCode);
    }

    [Fact]
    public void Resend_4xx_without_envelope_returns_empty_error_code()
    {
        var result = OtpErrorMapper.MapResendResult(400, null, "cid");
        Assert.False(result.Success);
        Assert.Equal("EmptyErrorCode", result.RawCode);
    }

    [Fact]
    public void Resend_correlation_id_is_preserved()
    {
        var result = OtpErrorMapper.MapResendResult(200, null, "cid-resend");
        Assert.Equal("cid-resend", result.CorrelationId);
    }
}
