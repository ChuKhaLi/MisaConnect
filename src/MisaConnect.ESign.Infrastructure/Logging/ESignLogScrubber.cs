using System.Text.RegularExpressions;

namespace MisaConnect.ESign.Infrastructure.Logging;

/// <summary>
/// Redacts sensitive substrings before they reach the logging boundary.
/// Constitution Principle VIII — bearer tokens, refresh tokens, the
/// <c>AuthorizationRM</c> header, base64 cert chunks, base64 doc bytes /
/// hashes / digests, and PII fields must never appear in logs.
/// </summary>
public static class ESignLogScrubber
{
    private const string Mask = "***";

    private static readonly Regex BearerPattern = new(
        @"Bearer\s+[A-Za-z0-9\-_=\.\+/]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AuthHeaderPattern = new(
        @"(AuthorizationRM|Authorization)\s*[:=]\s*[A-Za-z0-9\-_=\.\+/ ]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SensitiveJsonField = new(
        "\"(accessToken|remoteSigningAccessToken|refreshToken|authorizationRM|authorization|password|code|certificate|certiticateChain|certificateChain|fileToSign|documentBytes|documentHash|sh|digest|signature|signatureId|mainDom|document|fontData|signatureImage|logoImage|email|emailName|phoneNumber|firstName|lastName)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeviceJsonField = new(
        "\"device[A-Za-z0-9_]*\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Base64Chunk = new(
        @"[A-Za-z0-9+/]{32,}={0,2}",
        RegexOptions.Compiled);

    public static string Scrub(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = AuthHeaderPattern.Replace(input, m => $"{m.Groups[1].Value}: {Mask}");
        result = BearerPattern.Replace(result, $"Bearer {Mask}");
        result = SensitiveJsonField.Replace(result, m =>
        {
            var fieldStart = m.Value.IndexOf('"') + 1;
            var fieldEnd = m.Value.IndexOf('"', fieldStart);
            var fieldName = m.Value.Substring(fieldStart, fieldEnd - fieldStart);
            return $"\"{fieldName}\":\"{Mask}\"";
        });
        result = DeviceJsonField.Replace(result, m =>
        {
            var fieldStart = m.Value.IndexOf('"') + 1;
            var fieldEnd = m.Value.IndexOf('"', fieldStart);
            var fieldName = m.Value.Substring(fieldStart, fieldEnd - fieldStart);
            return $"\"{fieldName}\":\"{Mask}\"";
        });
        result = Base64Chunk.Replace(result, Mask);
        return result;
    }
}
