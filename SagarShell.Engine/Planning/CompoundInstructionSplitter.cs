using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Planning;

/// <summary>
/// Decomposes compound English sentences into an ordered list of individual action instructions.
/// </summary>
public static class CompoundInstructionSplitter
{
    // Explicit sequential connectors: "then", "and then", "after that", "followed by", ";"
    private static readonly Regex SequentialConnectorsRegex = new(
        @"(?:,\s*(?:and\s+)?then|\s+(?:and\s+)?then|,\s*after\s+that|\s+after\s+that|;\s*|\s+followed\s+by|\s+finally|\s+next)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Conjunctions with action verbs: "and (create|move|delete|compress|kill|start)..."
    private static readonly Regex ActionAndRegex = new(
        @"\s+and\s+(?=(?:create|make|delete|remove|rm|kill|terminate|move|mv|copy|cp|zip|compress|unzip|extract|start|stop|restart|open|run|navigate|cd|read|export|sort|show|list|get|find)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comma before action verbs: ", move them...", ", delete them...", ", compress them..."
    private static readonly Regex ActionCommaRegex = new(
        @",\s*(?=(?:create|make|delete|remove|rm|kill|terminate|move|mv|copy|cp|zip|compress|unzip|extract|start|stop|restart|open|run|navigate|cd|read|export|sort|show|list|get|find)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsCompound(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var sanitized = NaturalTextSanitizer.Sanitize(input);
        return SequentialConnectorsRegex.IsMatch(sanitized) 
            || ActionAndRegex.IsMatch(sanitized) 
            || ActionCommaRegex.IsMatch(sanitized);
    }

    public static List<string> Split(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        var sanitized = NaturalTextSanitizer.Sanitize(input);

        // Normalize sequential connectors and action conjunctions to standard split delimiter
        var normalized = SequentialConnectorsRegex.Replace(sanitized, " __SPLIT_STEP__ ");
        normalized = ActionAndRegex.Replace(normalized, " __SPLIT_STEP__ ");
        normalized = ActionCommaRegex.Replace(normalized, " __SPLIT_STEP__ ");

        var rawParts = normalized.Split("__SPLIT_STEP__", StringSplitOptions.RemoveEmptyEntries);
        var cleanSteps = new List<string>();

        foreach (var part in rawParts)
        {
            var cleaned = NaturalTextSanitizer.Sanitize(part.Trim());
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                cleanSteps.Add(cleaned);
            }
        }

        return cleanSteps;
    }
}
