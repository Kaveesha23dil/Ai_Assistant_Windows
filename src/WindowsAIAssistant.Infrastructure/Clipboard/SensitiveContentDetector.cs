using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Infrastructure.Clipboard;

public sealed class SensitiveContentDetector
{
    private static readonly Regex ApiKeyRegex = new(@"(sk-[a-zA-Z0-9]{10,}|[A-Za-z0-9_\-]{32,}|AKIA[0-9A-Z]{16})", RegexOptions.Compiled);
    private static readonly Regex JwtRegex = new(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled);
    private static readonly Regex PrivateKeyRegex = new(@"-----BEGIN (RSA|EC|DSA|OPENSSH) PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PasswordLikeRegex = new(@"(password|passwd|pwd)\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CreditCardRegex = new(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled);

    public bool ContainsSensitiveContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ApiKeyRegex.IsMatch(text)) return true;
        if (JwtRegex.IsMatch(text)) return true;
        if (PrivateKeyRegex.IsMatch(text)) return true;
        if (PasswordLikeRegex.IsMatch(text)) return true;
        if (CreditCardRegex.IsMatch(text)) return true;

        return false;
    }
}
