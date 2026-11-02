using MisaConnect.ESign.Application.Abstractions;

namespace MisaConnect.ESign.Application.UseCases;

public sealed class ResendOtp
{
    private readonly IMisaESignWireClient _wire;
    private readonly Func<string> _defaultLanguageAccessor;

    public ResendOtp(IMisaESignWireClient wire, Func<string> defaultLanguageAccessor)
    {
        _wire = wire;
        _defaultLanguageAccessor = defaultLanguageAccessor;
    }

    public Task<OtpResendResult> ExecuteAsync(string userName, string? language, CancellationToken ct)
    {
        var effectiveLanguage = string.IsNullOrEmpty(language) ? _defaultLanguageAccessor() : language;
        return _wire.ResendOtpAsync(userName, effectiveLanguage, ct);
    }
}
