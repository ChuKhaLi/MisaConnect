using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Domain.Webhook;

public sealed record BeginResult(string TransactionId, DocumentFormat Format);
