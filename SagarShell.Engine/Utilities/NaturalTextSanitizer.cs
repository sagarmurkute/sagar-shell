using System;
using System.Text.RegularExpressions;

namespace SagarShell.Engine.Utilities;

/// <summary>
/// Cleans, normalizes, and removes conversational pleasantries/noise from natural language commands.
/// </summary>
public static class NaturalTextSanitizer
{
    private static readonly Regex ConversationalPrefixRegex = new(
        @"^(?:(?:hey\s+(?:sagar|bot|shell|computer|assistant|ai)|sagar|please|can\s+you|could\s+you|would\s+you|i\s+want\s+(?:you\s+)?to|i\s+would\s+like\s+(?:you\s+)?to|i\s+need\s+(?:you\s+)?to|help\s+me(?:\s+to)?|kindly|just|tell\s+me|show\s+me\s+how\s+to|give\s+me)[\s,]*)+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingNoiseRegex = new(
        @"[\s,]*(?:please|for\s+me|thanks|thank\s+you)+[\s.!?]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtraSpacesRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var cleaned = input.Trim();

        // Remove conversational prefix
        cleaned = ConversationalPrefixRegex.Replace(cleaned, "");

        // Remove trailing conversational noise
        cleaned = TrailingNoiseRegex.Replace(cleaned, "");

        // Clean trailing punctuation without removing path dots like "..", "."
        if (!cleaned.EndsWith("\"") && !cleaned.EndsWith("'"))
        {
            cleaned = Regex.Replace(cleaned, @"[\?!;]+$", "");

            if (cleaned.EndsWith(".") && !cleaned.EndsWith("..") && cleaned.Length > 1 && !cleaned.EndsWith("/.") && !cleaned.EndsWith("\\."))
            {
                // Strip lone trailing period from conversational sentence if not part of a filename
                if (!Regex.IsMatch(cleaned, @"\b[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+$"))
                {
                    cleaned = cleaned[..^1];
                }
            }
        }

        // Collapse duplicate spaces
        cleaned = ExtraSpacesRegex.Replace(cleaned, " ").Trim();

        return cleaned;
    }
}
