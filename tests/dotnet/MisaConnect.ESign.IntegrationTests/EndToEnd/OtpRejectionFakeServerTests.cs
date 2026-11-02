using Microsoft.Extensions.DependencyInjection;
using MisaConnect.ESign.Client;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.IntegrationTests.EsignFake;
using Xunit;

namespace MisaConnect.ESign.IntegrationTests.EndToEnd;

public class OtpRejectionFakeServerTests
{
    private static async Task<IMisaESignClient> EstablishPendingChallenge(FakeMisaESignServer server, ServiceProvider sp)
    {
        var scope = sp.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IMisaESignClient>();
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            client.SignPdfAsync(TestPdfFixture.SampleRequest(), CancellationToken.None));
        _ = server;
        return client;
    }

    [Theory]
    [InlineData("1001", typeof(InvalidOtpException))]
    [InlineData("1002", typeof(ExpiredOtpException))]
    [InlineData("1003", typeof(ExhaustedOtpAttemptsException))]
    public async Task Rejection_categories_surface_distinct_typed_exceptions(string code, Type expected)
    {
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.NextTwoFactorErrorCode = code;
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            client.SignInWithOtpAsync("WRONG-OTP", OtpDeliveryChannel.SmsOrEmail, false, CancellationToken.None));
        Assert.IsType(expected, ex);
        Assert.IsAssignableFrom<AuthenticationFailedException>(ex);
    }

    [Fact]
    public async Task Captured_wire_body_does_not_contain_otp_code_in_logs()
    {
        // The recorded body itself contains the raw code (the wire HAS to send it).
        // The point of SC-012 is the LOG capture — verified by the scrubber unit tests.
        // Here we ensure that the OTP code value never appears in the exception detail.
        await using var server = await FakeMisaESignServer.StartAsync();
        server.Configure.LoginRequires2FAOnFirstCall = true;
        server.Configure.NextTwoFactorErrorCode = "1001";
        await using var sp = TestServiceProvider.Build(server.BaseUrl);
        var client = await EstablishPendingChallenge(server, sp);

        var ex = await Assert.ThrowsAsync<InvalidOtpException>(() =>
            client.SignInWithOtpAsync("super-secret-otp-9999", OtpDeliveryChannel.SmsOrEmail, false, CancellationToken.None));

        Assert.DoesNotContain("super-secret-otp-9999", ex.Detail);
        Assert.DoesNotContain("super-secret-otp-9999", ex.Message);
    }
}
