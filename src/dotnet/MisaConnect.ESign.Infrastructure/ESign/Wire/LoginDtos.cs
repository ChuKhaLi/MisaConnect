using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class LoginRequestDto
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

internal sealed class LoginResponseDto
{
    [JsonPropertyName("status")]
    public LoginStatusBlockDto? Status { get; set; }

    [JsonPropertyName("data")]
    public LoginDataBlockDto? Data { get; set; }
}

internal sealed class LoginStatusBlockDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("devMsg")]
    public string? DevMsg { get; set; }

    [JsonPropertyName("userMsg")]
    public string? UserMsg { get; set; }
}

internal sealed class LoginDataBlockDto
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("remoteSigningAccessToken")]
    public string? RemoteSigningAccessToken { get; set; }

    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("user")]
    public LoginUserBlockDto? User { get; set; }
}

internal sealed class LoginUserBlockDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}
