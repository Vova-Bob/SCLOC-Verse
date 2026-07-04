using System.Text.RegularExpressions;

namespace SCLOCVerse.ControlCenter.Services;

/// <summary>
/// Мінімальний санітайзер PII для текстових полів Knowledge Engine (Slice 2).
/// Видаляє: email, IPv4/IPv6, JWT/Bearer токени, Discord snowflake, MAC-адреси.
/// НЕ вирішує всі можливі випадки — лише базовий захід (Стаття 4).
/// </summary>
public static class PiiSanitizer
{
    // RFC 5322 oversimplified — достатньо для базового захисту.
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // IPv4.
    private static readonly Regex Ipv4Regex = new(
        @"\b(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // MAC-адреса.
    private static readonly Regex MacRegex = new(
        @"\b(?:[0-9A-Fa-f]{2}[:-]){5}(?:[0-9A-Fa-f]{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Discord snowflake — 17-20 цифр.
    private static readonly Regex DiscordSnowflakeRegex = new(
        @"\b\d{17,20}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // JWT / Bearer token.
    private static readonly Regex JwtRegex = new(
        @"\beyJ[a-zA-Z0-9_-]*\.[a-zA-Z0-9_-]*\.[a-zA-Z0-9_-]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[A-Za-z0-9_\-\.]+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Санітизує вхідний текст, замінюючи виявлені PII на маркер [REDACTED].
    /// Null або whitespace повертає як є.
    /// </summary>
    public static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var s = input;
        s = BearerRegex.Replace(s, "[REDACTED]");
        s = JwtRegex.Replace(s, "[REDACTED]");
        s = EmailRegex.Replace(s, "[REDACTED]");
        s = Ipv4Regex.Replace(s, "[REDACTED]");
        s = MacRegex.Replace(s, "[REDACTED]");
        s = DiscordSnowflakeRegex.Replace(s, "[REDACTED]");
        return s;
    }
}
