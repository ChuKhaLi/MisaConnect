namespace MisaConnect.ESign.Domain.Authentication;

/// <summary>
/// Discriminator for MISA's <c>otpType</c> field on <c>/two-factor-auth</c>.
/// The wire value is the integer assigned to each member; System.Text.Json's
/// default integer-enum behavior handles serialization.
/// </summary>
public enum OtpDeliveryChannel : byte
{
    SmsOrEmail = 0,
    Authenticator = 1,
}
