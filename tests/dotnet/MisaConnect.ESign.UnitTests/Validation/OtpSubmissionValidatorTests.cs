using MisaConnect.ESign.Application.Abstractions;
using MisaConnect.ESign.Application.Validation;
using MisaConnect.ESign.Domain.Authentication;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.UnitTests.TestSupport;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Validation;

public class OtpSubmissionValidatorTests
{
    private static OtpSubmissionValidator Build() =>
        new(new StubCorrelationIdAccessor("cid"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Throws_when_code_is_blank(string? code)
    {
        var sut = Build();
        var submission = new OtpSubmission(code!, OtpDeliveryChannel.SmsOrEmail, false);
        Assert.Throws<ESignGeneralException>(() => sut.Validate(submission));
    }

    [Fact]
    public void Accepts_non_empty_code()
    {
        var sut = Build();
        sut.Validate(new OtpSubmission("123456", OtpDeliveryChannel.SmsOrEmail, false));
        sut.Validate(new OtpSubmission("xyz", OtpDeliveryChannel.Authenticator, true));
    }
}
