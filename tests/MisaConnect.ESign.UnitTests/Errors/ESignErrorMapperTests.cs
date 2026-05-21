using System.Net;
using MisaConnect.ESign.Application.Errors;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Errors;

public class ESignErrorMapperTests
{
    [Fact]
    public void Login_122_marks_requires_2fa()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: "122", DevMsg: null, UserMsg: null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointLogin, 400, envelope, "cid");
        var auth = Assert.IsType<AuthenticationFailedException>(ex);
        Assert.True(auth.Requires2FA);
        Assert.Equal("122", auth.RawCode);
        Assert.Equal("cid", auth.CorrelationId);
    }

    [Fact]
    public void Login_122_populates_username_from_request()
    {
        var envelope = new ResponseError(Error: true, ErrorCode: "122", DevMsg: null, UserMsg: null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointLogin, 400, envelope, "cid", userName: "alice");
        var auth = Assert.IsType<AuthenticationFailedException>(ex);
        Assert.True(auth.Requires2FA);
        Assert.Equal("alice", auth.Username);
    }

    [Fact]
    public void Login_non_122_does_not_carry_username()
    {
        var envelope = new ResponseError(true, "InvalidPassword", null, null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointLogin, 401, envelope, "cid", userName: "alice");
        var auth = Assert.IsType<AuthenticationFailedException>(ex);
        Assert.Equal(string.Empty, auth.Username);
    }

    [Fact]
    public void Login_generic_4xx_is_authentication_failure()
    {
        var envelope = new ResponseError(true, "InvalidPassword", null, null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointLogin, 401, envelope, "cid");
        Assert.IsType<AuthenticationFailedException>(ex);
        Assert.False(((AuthenticationFailedException)ex).Requires2FA);
    }

    [Fact]
    public void Refresh_4xx_does_not_loop()
    {
        var envelope = new ResponseError(true, "ExpiredRefresh", null, null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointRefresh, 400, envelope, "cid");
        Assert.IsType<AuthenticationFailedException>(ex);
    }

    [Fact]
    public void Hash_invalid_certificate_maps_to_hashrejected()
    {
        var envelope = new ResponseError(true, "InvalidCertificate", null, null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointHash, 400, envelope, "cid");
        var general = Assert.IsType<ESignGeneralException>(ex);
        Assert.Equal(ESignErrorCategory.HashRejected, general.Category);
        Assert.Equal("InvalidCertificate", general.RawCode);
    }

    [Fact]
    public void SignHash_user_not_connected_sets_requires_user_setup()
    {
        var envelope = new ResponseError(true, "UserNotConnected", DevMsg: "User has not connected eSign account", UserMsg: null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointSignHash, 400, envelope, "cid");
        var rejection = Assert.IsType<SignRejectedException>(ex);
        Assert.True(rejection.RequiresUserCertSetup);
    }

    [Fact]
    public void SignHash_other_codes_dont_set_requires_user_setup()
    {
        var envelope = new ResponseError(true, "CertRevoked", DevMsg: "Cert revoked mid-flow", UserMsg: null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointSignHash, 400, envelope, "cid");
        var rejection = Assert.IsType<SignRejectedException>(ex);
        Assert.False(rejection.RequiresUserCertSetup);
    }

    [Fact]
    public void Attachment_invalid_signature_maps_to_attachmentrejected()
    {
        var envelope = new ResponseError(true, "InvalidSignature", null, null);
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointAttachment, 400, envelope, "cid");
        var general = Assert.IsType<ESignGeneralException>(ex);
        Assert.Equal(ESignErrorCategory.AttachmentRejected, general.Category);
    }

    [Fact]
    public void Status_500_maps_to_transport()
    {
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointSignStatus, 500, null, "cid", attemptCount: 3, lastStatusCode: HttpStatusCode.InternalServerError);
        var t = Assert.IsType<ESignTransportException>(ex);
        Assert.Equal(HttpStatusCode.InternalServerError, t.LastStatusCode);
        Assert.Equal(3, t.AttemptCount);
    }

    [Fact]
    public void Certificates_401_after_refresh_fails_authentication()
    {
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointCertificates, 401, null, "cid");
        Assert.IsType<AuthenticationFailedException>(ex);
    }

    [Fact]
    public void Status_terminal_failed_maps()
    {
        var ex = ESignErrorMapper.MapStatusTerminal("tx-1", SignStatus.FAILED, "ResultBad", "Something failed", "cid");
        var terminal = Assert.IsType<SignTerminalStateException>(ex);
        Assert.Equal(SignStatus.FAILED, terminal.TerminalStatus);
        Assert.Equal("tx-1", terminal.TransactionId);
        Assert.Equal("ResultBad", terminal.RawCode);
    }

    [Fact]
    public void Default_detail_omits_raw_messages()
    {
        var envelope = new ResponseError(true, "X", DevMsg: "sensitive-dev", UserMsg: "sensitive-user");
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointHash, 400, envelope, "cid", includeRawErrorMessage: false);
        Assert.DoesNotContain("sensitive-dev", ex.Detail);
        Assert.DoesNotContain("sensitive-user", ex.Detail);
    }

    [Fact]
    public void Opt_in_includes_raw_messages()
    {
        var envelope = new ResponseError(true, "X", DevMsg: "sensitive-dev", UserMsg: "sensitive-user");
        var ex = ESignErrorMapper.Map(ESignErrorMapper.EndpointHash, 400, envelope, "cid", includeRawErrorMessage: true);
        Assert.Contains("sensitive-dev", ex.Detail);
        Assert.Contains("sensitive-user", ex.Detail);
    }
}
