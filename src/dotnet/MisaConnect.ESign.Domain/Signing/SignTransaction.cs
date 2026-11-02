namespace MisaConnect.ESign.Domain.Signing;

public sealed record SignTransaction(string TransactionId, DateTimeOffset SubmittedAtUtc);
