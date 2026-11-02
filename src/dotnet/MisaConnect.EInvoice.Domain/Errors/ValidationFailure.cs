namespace MisaConnect.EInvoice.Domain.Errors;

public sealed record ValidationFailure(string FieldPath, string Message, string? RuleId = null);
