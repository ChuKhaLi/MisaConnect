using System.Net;
using MisaConnect.ESign.Domain.Documents;
using MisaConnect.ESign.Domain.Errors;
using MisaConnect.ESign.Domain.Signing;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Errors;

public class FormatPropertyContractTests
{
    [Theory]
    [InlineData(DocumentFormat.Unknown)]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xml)]
    [InlineData(DocumentFormat.Word)]
    [InlineData(DocumentFormat.Excel)]
    public void ESignGeneralException_carries_supplied_format(DocumentFormat format)
    {
        var ex = new ESignGeneralException(ESignErrorCategory.HashRejected, "code", "detail", "cid", format: format);
        Assert.Equal(format, ex.Format);
    }

    [Theory]
    [InlineData(DocumentFormat.Pdf)]
    [InlineData(DocumentFormat.Xml)]
    public void AuthenticationFailedException_carries_supplied_format(DocumentFormat format)
    {
        var ex = new AuthenticationFailedException("code", "detail", "cid", format: format);
        Assert.Equal(format, ex.Format);
    }

    [Fact]
    public void ESignTransportException_carries_supplied_format()
    {
        var ex = new ESignTransportException(HttpStatusCode.InternalServerError, 1, "detail", "cid", format: DocumentFormat.Word);
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }

    [Fact]
    public void NoActiveCertificateException_carries_supplied_format()
    {
        var ex = new NoActiveCertificateException("detail", "cid", format: DocumentFormat.Excel);
        Assert.Equal(DocumentFormat.Excel, ex.Format);
    }

    [Fact]
    public void SignRejectedException_carries_supplied_format()
    {
        var ex = new SignRejectedException("code", "detail", "cid", format: DocumentFormat.Xml);
        Assert.Equal(DocumentFormat.Xml, ex.Format);
    }

    [Fact]
    public void SignTerminalStateException_carries_supplied_format()
    {
        var ex = new SignTerminalStateException(SignStatus.FAILED, "tx", "code", "detail", "cid", format: DocumentFormat.Word);
        Assert.Equal(DocumentFormat.Word, ex.Format);
    }

    [Fact]
    public void SignTimeoutException_carries_supplied_format()
    {
        var ex = new SignTimeoutException("tx", TimeSpan.FromSeconds(1), "detail", "cid", format: DocumentFormat.Pdf);
        Assert.Equal(DocumentFormat.Pdf, ex.Format);
    }

    [Fact]
    public void InvalidOtpException_carries_supplied_format()
    {
        var ex = new InvalidOtpException("code", "detail", "cid", format: DocumentFormat.Pdf);
        Assert.Equal(DocumentFormat.Pdf, ex.Format);
    }

    [Fact]
    public void Default_format_on_existing_construction_paths_is_Unknown()
    {
        var ex = new ESignGeneralException(ESignErrorCategory.MisaUnknown, "code", "detail", "cid");
        Assert.Equal(DocumentFormat.Unknown, ex.Format);
    }
}
