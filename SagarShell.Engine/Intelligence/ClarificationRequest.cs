using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// A clarifying question with structured options generated when a command is ambiguous or low-confidence.
/// </summary>
public class ClarificationRequest
{
    public required string OriginalInput { get; set; }
    public required string Question { get; set; }
    public List<ClarificationOption> Options { get; set; } = new();
    public string? DisambiguationType { get; set; }
    public double OriginalConfidence { get; set; } = 0.50;
    public TranslationResult? BaseTranslation { get; set; }

    public ClarificationOption? GetOption(int index) =>
        Options.FirstOrDefault(o => o.Index == index);

    /// <summary>
    /// Intelligently matches a user's natural response to one of the clarification options.
    /// Supports number indices, ordinals ("first", "second", "last"), and fuzzy keyword matching.
    /// </summary>
    public ClarificationOption? FindOptionByQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var clean = query.Trim().ToLowerInvariant();

        // 1. Direct number or "option N" / "opt N" / "#N"
        var numberMatch = Regex.Match(clean, @"^(?:option\s+|opt\s+|#|no\.\s*)?(?<num>\d+)$", RegexOptions.IgnoreCase);
        if (numberMatch.Success && int.TryParse(numberMatch.Groups["num"].Value, out int idx))
        {
            var opt = GetOption(idx);
            if (opt != null) return opt;
        }

        // 2. Strict Ordinals
        if (Regex.IsMatch(clean, @"^(?:the\s+)?(?:first|1st|option\s+1|number\s+1)(?:\s+one|\s+option)?$", RegexOptions.IgnoreCase))
        {
            if (Options.Count > 0) return Options[0];
        }
        if (Regex.IsMatch(clean, @"^(?:the\s+)?(?:second|2nd|option\s+2|number\s+2)(?:\s+one|\s+option)?$", RegexOptions.IgnoreCase))
        {
            if (Options.Count > 1) return Options[1];
        }
        if (Regex.IsMatch(clean, @"^(?:the\s+)?(?:third|3rd|option\s+3|number\s+3)(?:\s+one|\s+option)?$", RegexOptions.IgnoreCase))
        {
            if (Options.Count > 2) return Options[2];
        }
        if (Regex.IsMatch(clean, @"^(?:the\s+)?(?:fourth|4th|option\s+4|number\s+4)(?:\s+one|\s+option)?$", RegexOptions.IgnoreCase))
        {
            if (Options.Count > 3) return Options[3];
        }
        if (Regex.IsMatch(clean, @"^(?:the\s+)?(?:last|final)(?:\s+one|\s+option)?$", RegexOptions.IgnoreCase))
        {
            if (Options.Count > 0) return Options[^1];
        }

        // 3. Keyword Substring Match in Label or Description
        var words = clean.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && w != "the" && w != "and" && w != "for" && w != "with" && w != "please")
            .ToList();

        ClarificationOption? bestMatch = null;
        int highestMatches = 0;

        foreach (var opt in Options)
        {
            int matches = 0;
            var labelLower = opt.Label.ToLowerInvariant();
            var descLower = opt.Description.ToLowerInvariant();

            // Direct substring check
            if (labelLower.Contains(clean) || clean.Contains(labelLower))
            {
                return opt;
            }

            foreach (var word in words)
            {
                if (labelLower.Contains(word)) matches += 2;
                if (descLower.Contains(word)) matches += 1;
            }

            if (matches > highestMatches)
            {
                highestMatches = matches;
                bestMatch = opt;
            }
        }

        return highestMatches > 0 ? bestMatch : null;
    }

    public override string ToString()
    {
        var lines = new List<string>
        {
            $"❓ Clarification Needed: \"{OriginalInput}\"",
            $"   {Question}"
        };

        foreach (var opt in Options)
        {
            lines.Add($"   [{opt.Index}] {opt.Label} ({opt.Risk}) - {opt.Description}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
