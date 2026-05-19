using MisaConnect.ESign.Infrastructure.Configuration;
using Xunit;

namespace MisaConnect.ESign.UnitTests.Configuration;

public class MisaESignOptionsValidatorTests
{
    private static MisaESignOptions ValidOptions() => new()
    {
        Environment = ESignEnvironment.Sandbox,
        BaseUrl = "https://sandbox.example.com/",
        ClientId = "cid",
        ClientKey = "key",
        UserName = "user",
        Password = "pass",
    };

    [Fact]
    public void Defaults_with_complete_inputs_pass()
    {
        var validator = new MisaESignOptionsValidator();
        var result = validator.Validate(null, ValidOptions());
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? Array.Empty<string>()));
    }

    [Fact]
    public void Missing_baseurl_fails()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.BaseUrl = "";
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Non_https_baseurl_fails()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.BaseUrl = "http://sandbox.example.com/";
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Sandbox_host_equals_production_fails()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.Environment = ESignEnvironment.Sandbox;
        opts.BaseUrl = $"https://{MisaESignOptions.ProductionHost}/";
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Production_host_not_production_fails()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.Environment = ESignEnvironment.Production;
        opts.BaseUrl = "https://sandbox.example.com/";
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Production_with_production_host_passes()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.Environment = ESignEnvironment.Production;
        opts.BaseUrl = $"https://{MisaESignOptions.ProductionHost}/";
        var result = validator.Validate(null, opts);
        Assert.True(result.Succeeded, string.Join("; ", result.Failures ?? Array.Empty<string>()));
    }

    [Fact]
    public void Missing_credentials_fail()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.UserName = "";
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Polling_interval_must_be_positive()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.Polling.Interval = TimeSpan.Zero;
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Polling_totaltimeout_must_exceed_interval()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.Polling.Interval = TimeSpan.FromSeconds(10);
        opts.Polling.TotalTimeout = TimeSpan.FromSeconds(5);
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Transportretry_maxdelay_must_be_at_least_basedelay()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.TransportRetry.BaseDelay = TimeSpan.FromSeconds(2);
        opts.TransportRetry.MaxDelay = TimeSpan.FromMilliseconds(200);
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Transportretry_maxattempts_must_be_at_least_1()
    {
        var validator = new MisaESignOptionsValidator();
        var opts = ValidOptions();
        opts.TransportRetry.MaxAttempts = 0;
        var result = validator.Validate(null, opts);
        Assert.True(result.Failed);
    }
}
