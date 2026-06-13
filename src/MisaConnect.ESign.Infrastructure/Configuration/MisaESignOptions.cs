namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignOptions
{
    public const string SectionName = "Misa:ESign";
    public const string ProductionHost = "esignapp.misa.vn";

    public ESignEnvironment Environment { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Overrides where the login and two-factor endpoints are served, relative
    /// to the host root derived from <see cref="BaseUrl"/>. <c>null</c> (default)
    /// derives from <see cref="Environment"/>: <see cref="ESignEnvironment.Sandbox"/>
    /// serves them under <c>/webdev/</c>, <see cref="ESignEnvironment.Production"/>
    /// at the host root (per the official API doc). <c>true</c> forces
    /// <c>/webdev/</c>; <c>false</c> forces the host root. Does not affect the
    /// refresh-token / resend-otp endpoints (always under <c>/webdev/</c>) or the
    /// ESRM endpoints (always at the host root).
    /// </summary>
    public bool? AuthUnderWebdev { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public MisaESignPollingOptions Polling { get; set; } = new();
    public MisaESignTransportRetryOptions TransportRetry { get; set; } = new();
    public MisaESignErrorOptions Errors { get; set; } = new();
    public MisaESignOtpOptions Otp { get; set; } = new();
    public MisaESignWebhookOptions Webhook { get; set; } = new();
}

public sealed class MisaESignOtpOptions
{
    /// <summary>
    /// Default value supplied to <c>/resend-otp-auth</c> when the consumer's
    /// <c>ResendOtpAsync</c> call does not override the language. The SDK does
    /// not validate the value — MISA is the authority on supported values.
    /// </summary>
    public string DefaultResendLanguage { get; set; } = "en-US";
}

public sealed class MisaESignPollingOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class MisaESignTransportRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class MisaESignErrorOptions
{
    public bool IncludeRawErrorMessage { get; set; }
}
