using System;
using System.Text.RegularExpressions;

namespace SagarShell.Engine.Memory;

/// <summary>
/// Strips sensitive credentials, API keys, passwords, and private tokens before persisting history to disk.
/// </summary>
public static class PrivacySanitizer
{
    private static readonly Regex OpenAIKeyRegex = new(
        @"\b(?:sk-[a-zA-Z0-9_-]{20,})\b",
        RegexOptions.Compiled);

    private static readonly Regex AwsKeyRegex = new(
        @"\b(?:AKIA[0-9A-Z]{16})\b",
        RegexOptions.Compiled);

    private static readonly Regex GitHubTokenRegex = new(
        @"\b(?:ghp_[a-zA-Z0-9]{36}|github_pat_[a-zA-Z0-9_]{40,})\b",
        RegexOptions.Compiled);

    private static readonly Regex JwtTokenRegex = new(
        @"\beyJ[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}\.[a-zA-Z0-9_-]{10,}\b",
        RegexOptions.Compiled);

    private static readonly Regex BearerAuthRegex = new(
        @"(?:Bearer\s+)[a-zA-Z0-9_\-\.~+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PasswordParamRegex = new(
        @"(?:-Password|--password|-pwd|\bpassword\b|\bsecret\b)\s*[:= ]\s*(?:'[^']+'|""[^""]+""|[^\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionStringPasswordRegex = new(
        @"(?:Password|pwd)\s*=\s*[^;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PrivateKeyBlockRegex = new(
        @"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[\s\S]+?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var result = input;

        result = OpenAIKeyRegex.Replace(result, "[REDACTED_OPENAI_KEY]");
        result = AwsKeyRegex.Replace(result, "[REDACTED_AWS_KEY]");
        result = GitHubTokenRegex.Replace(result, "[REDACTED_GITHUB_TOKEN]");
        result = JwtTokenRegex.Replace(result, "[REDACTED_JWT_TOKEN]");
        result = BearerAuthRegex.Replace(result, "Bearer [REDACTED_BEARER_TOKEN]");
        result = PasswordParamRegex.Replace(result, "-Password [REDACTED_PASSWORD]");
        result = ConnectionStringPasswordRegex.Replace(result, "Password=[REDACTED]");
        result = PrivateKeyBlockRegex.Replace(result, "[REDACTED_PRIVATE_KEY]");

        return result;
    }
}
