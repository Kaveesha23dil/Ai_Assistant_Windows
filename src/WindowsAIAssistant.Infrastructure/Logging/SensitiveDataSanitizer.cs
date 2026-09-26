using System.Text.RegularExpressions;

namespace WindowsAIAssistant.Infrastructure.Logging;

/// <summary>
/// A small, defensive helper that redacts values that look like credentials from text
/// destined for a log sink. It is intentionally not a DLP engine: it covers the common
/// token shapes the assistant can encounter and leaves ordinary text untouched.
/// </summary>
public static class SensitiveDataSanitizer
{
    public const string RedactedPlaceholder = "***REDACTED***";

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Authorization: Bearer <token> or "authorization": "<token>"
    private static readonly Regex BearerPattern = new(
        @"\b(bearer|basic)\s+[A-Za-z0-9\-._~+/]+=*",
        Options,
        TimeSpan.FromSeconds(1));

    // key = value / key: value for well-known sensitive key names.
    private static readonly Regex KeyValuePattern = new(
        @"\b(api[-_ ]?key|apikey|secret[-_ ]?key|client[-_ ]?secret|access[-_ ]?token|refresh[-_ ]?token|auth[-_ ]?token|authorization|password|passwd|pwd|token)\b(\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;&]+)",
        Options,
        TimeSpan.FromSeconds(1));

    // Vendor-style API keys such as sk-..., prefixed with a provider name.
    private static readonly Regex PrefixedKeyPattern = new(
        @"\b(?:sk|pk|rk|ak)[-_][A-Za-z0-9_\-]{4,}",
        Options,
        TimeSpan.FromSeconds(1));

    // JSON Web Tokens.
    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*",
        Options,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Returns <paramref name="value"/> with credential-like substrings replaced by
    /// <see cref="RedactedPlaceholder"/>. Returns <see langword="null"/> for null input.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var sanitized = BearerPattern.Replace(value, $"{RedactedPlaceholder}");
        sanitized = KeyValuePattern.Replace(sanitized, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{RedactedPlaceholder}");
        sanitized = PrefixedKeyPattern.Replace(sanitized, RedactedPlaceholder);
        sanitized = JwtPattern.Replace(sanitized, RedactedPlaceholder);
        return sanitized;
    }
}
