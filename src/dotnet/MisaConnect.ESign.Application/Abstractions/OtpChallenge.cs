namespace MisaConnect.ESign.Application.Abstractions;

public sealed record OtpChallenge(string UserName, string CorrelationId);
