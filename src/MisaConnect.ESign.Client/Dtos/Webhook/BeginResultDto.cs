using MisaConnect.ESign.Domain.Documents;

namespace MisaConnect.ESign.Client.Dtos.Webhook;

public sealed record BeginResultDto(string TransactionId, DocumentFormat Format);
