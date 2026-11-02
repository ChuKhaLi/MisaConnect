using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MisaConnect.EInvoice.Infrastructure.Logging;

/// <summary>
/// Replaces values for sensitive header keys and JSON body fields with <c>***</c>
/// before they reach the logging boundary. Constitution Principle IV — bearer
/// tokens, passwords, app IDs, and tax codes MUST never appear in logs.
/// </summary>
public static class MeInvoiceLogScrubber
{
    private const string Mask = "***";

    private static readonly HashSet<string> SensitiveHeaderKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Bearer",
        "Taxcode",
        "Password",
        "Token",
    };

    private static readonly Regex SensitiveJsonField = new(
        "\"(password|appid|app_id|app-id|token|taxcode|authorization|bearer)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Redact(IDictionary<string, string?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var key in new List<string>(headers.Keys))
        {
            if (SensitiveHeaderKeys.Contains(key))
            {
                headers[key] = Mask;
            }
        }
    }

    public static string Redact(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return SensitiveJsonField.Replace(body, m =>
        {
            var fieldNameStart = m.Value.IndexOf('"') + 1;
            var fieldNameEnd = m.Value.IndexOf('"', fieldNameStart);
            var fieldName = m.Value.Substring(fieldNameStart, fieldNameEnd - fieldNameStart);
            return $"\"{fieldName}\":\"{Mask}\"";
        });
    }
}
