namespace MisaConnect.EInvoice.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
