namespace MisaConnect.ESign.Domain.Webhook;

public sealed record WebhookAck(string ErrorCode, string DevMsg, string UserMsg)
{
    public static WebhookAck Success(string code) =>
        new(code, DevMsg: "Webhook accepted.", UserMsg: "Webhook accepted.");

    public static WebhookAck Failure(string code, string devMsg, string userMsg) =>
        new(code, devMsg, userMsg);
}
