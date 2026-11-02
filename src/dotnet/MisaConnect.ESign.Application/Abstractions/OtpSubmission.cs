using MisaConnect.ESign.Domain.Authentication;

namespace MisaConnect.ESign.Application.Abstractions;

public sealed record OtpSubmission(string Code, OtpDeliveryChannel OtpType, bool Remember);
