using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.UseCases;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Authentication;

public class ResendOtpTests
{
    [Fact]
    public async Task Default_language_is_used_when_caller_passes_null()
    {
        string? capturedLanguage = null;
        var wire = new StubWireClient
        {
            OnResendOtp = (_, lang, _) =>
            {
                capturedLanguage = lang;
                return Task.FromResult(new OtpResendResult(true, null, null, null, "cid"));
            },
        };
        var sut = new ResendOtp(wire, () => "en-US");
        var result = await sut.ExecuteAsync("alice", language: null, ct: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("en-US", capturedLanguage);
    }

    [Fact]
    public async Task Override_language_is_threaded_verbatim()
    {
        string? capturedLanguage = null;
        var wire = new StubWireClient
        {
            OnResendOtp = (_, lang, _) =>
            {
                capturedLanguage = lang;
                return Task.FromResult(new OtpResendResult(true, null, null, null, "cid"));
            },
        };
        var sut = new ResendOtp(wire, () => "en-US");
        await sut.ExecuteAsync("alice", language: "vi-VN", ct: CancellationToken.None);

        Assert.Equal("vi-VN", capturedLanguage);
    }

    [Fact]
    public async Task Typed_failure_result_is_returned_without_throwing()
    {
        var wire = new StubWireClient
        {
            OnResendOtp = (_, _, _) =>
                Task.FromResult(new OtpResendResult(false, "RateLimited", "please wait", "too soon", "cid")),
        };
        var sut = new ResendOtp(wire, () => "en-US");
        var result = await sut.ExecuteAsync("alice", language: null, ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RateLimited", result.RawCode);
        Assert.Equal("please wait", result.UserMsg);
        Assert.Equal("too soon", result.DevMsg);
    }
}
