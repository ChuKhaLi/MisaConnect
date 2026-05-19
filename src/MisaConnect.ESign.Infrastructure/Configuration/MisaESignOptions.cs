namespace MisaConnect.ESign.Infrastructure.Configuration;

public sealed class MisaESignOptions
{
    public const string SectionName = "Misa:ESign";
    public const string ProductionHost = "esignapp.misa.vn";

    public ESignEnvironment Environment { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public MisaESignPollingOptions Polling { get; set; } = new();
    public MisaESignTransportRetryOptions TransportRetry { get; set; } = new();
    public MisaESignErrorOptions Errors { get; set; } = new();
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
