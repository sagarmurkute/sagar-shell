using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Memory;

/// <summary>
/// Natural Language History Search Engine.
/// Parses queries like "show the command I used yesterday to find large files" and filters persistent history.
/// </summary>
public static class NaturalHistorySearchEngine
{
    private static readonly Regex HistorySearchIntentRegex = new(
        @"(?:show|find|search|what\s+was|get|list|display)\s+(?:the\s+)?(?:command[s]?|history|queries|operations?)(?:\s+(?:i\s+used|from|in|for|matching|about)\s+)?(?<rest>.*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record HistorySearchResult(List<HistoricalCommandRecord> Matches, string QueryExplanation);

    public static bool IsHistorySearchQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var lower = input.ToLowerInvariant().Trim();
        return lower.StartsWith("history")
            || lower.StartsWith("search history")
            || lower.Contains("command i used")
            || lower.Contains("commands from")
            || lower.Contains("history from")
            || lower.Contains("what was the command")
            || HistorySearchIntentRegex.IsMatch(input);
    }

    public static HistorySearchResult Search(string query, IEnumerable<HistoricalCommandRecord> history)
    {
        var records = history.ToList();
        if (records.Count == 0)
        {
            return new HistorySearchResult(new List<HistoricalCommandRecord>(), "No persistent history records found.");
        }

        var lower = query.ToLowerInvariant().Trim();

        // 1. Time Range Extraction
        var (startTime, endTime, timeDesc) = ExtractTimeRange(lower);

        var filtered = records.AsEnumerable();

        if (startTime.HasValue)
        {
            filtered = filtered.Where(r => r.Timestamp >= startTime.Value);
        }
        if (endTime.HasValue)
        {
            filtered = filtered.Where(r => r.Timestamp <= endTime.Value);
        }

        // 2. Status Filtering ("failed commands", "successful")
        if (lower.Contains("failed") || lower.Contains("error"))
        {
            filtered = filtered.Where(r => !r.Success);
        }
        else if (lower.Contains("successful") || lower.Contains("succeeded"))
        {
            filtered = filtered.Where(r => r.Success);
        }

        // 3. Category Inference
        var targetCategory = InferCategory(lower);
        if (targetCategory.HasValue)
        {
            // Boost or filter by category
            var catMatches = filtered.Where(r => r.Category == targetCategory.Value).ToList();
            if (catMatches.Count > 0)
            {
                filtered = catMatches;
            }
        }

        // 4. Keyword / Entity Matching
        var keywords = ExtractKeywords(lower);
        if (keywords.Count > 0)
        {
            filtered = filtered.Where(r =>
            {
                var combined = $"{r.RawInput} {r.PowerShellScript} {r.Explanation} {r.WorkingDirectory}".ToLowerInvariant();
                return keywords.Any(k => combined.Contains(k));
            });
        }

        var results = filtered.OrderByDescending(r => r.Timestamp).Take(20).ToList();
        var explanation = $"Found {results.Count} matching command(s)" +
                          (!string.IsNullOrEmpty(timeDesc) ? $" from {timeDesc}" : "") +
                          (keywords.Count > 0 ? $" matching [{string.Join(", ", keywords)}]" : "") + ".";

        return new HistorySearchResult(results, explanation);
    }

    private static (DateTime? Start, DateTime? End, string? Description) ExtractTimeRange(string input)
    {
        var now = DateTime.UtcNow;

        if (input.Contains("yesterday"))
        {
            var yesterdayStart = now.Date.AddDays(-1);
            var yesterdayEnd = now.Date;
            return (yesterdayStart, yesterdayEnd, "yesterday");
        }

        if (input.Contains("today"))
        {
            return (now.Date, null, "today");
        }

        if (input.Contains("last week") || input.Contains("past week"))
        {
            return (now.AddDays(-7), null, "the past week");
        }

        if (input.Contains("last month") || input.Contains("past month"))
        {
            return (now.AddDays(-30), null, "the past month");
        }

        var daysMatch = Regex.Match(input, @"(?:past|last)\s+(?<days>\d+)\s+days?", RegexOptions.IgnoreCase);
        if (daysMatch.Success && int.TryParse(daysMatch.Groups["days"].Value, out var days))
        {
            return (now.AddDays(-days), null, $"the past {days} days");
        }

        var hoursMatch = Regex.Match(input, @"(?:past|last)\s+(?<hours>\d+)\s+hours?", RegexOptions.IgnoreCase);
        if (hoursMatch.Success && int.TryParse(hoursMatch.Groups["hours"].Value, out var hours))
        {
            return (now.AddHours(-hours), null, $"the past {hours} hours");
        }

        return (null, null, null);
    }

    private static IntentCategory? InferCategory(string input)
    {
        if (input.Contains("file") || input.Contains("folder") || input.Contains("dir") || input.Contains("zip") || input.Contains("download"))
            return IntentCategory.FileSystem;

        if (input.Contains("process") || input.Contains("cpu") || input.Contains("ram") || input.Contains("kill") || input.Contains("memory"))
            return IntentCategory.ProcessManagement;

        if (input.Contains("port") || input.Contains("ip") || input.Contains("ping") || input.Contains("wifi") || input.Contains("dns"))
            return IntentCategory.Network;

        if (input.Contains("git") || input.Contains("commit") || input.Contains("repo") || input.Contains("branch"))
            return IntentCategory.GitOperations;

        if (input.Contains("service"))
            return IntentCategory.WindowsServices;

        return null;
    }

    private static List<string> ExtractKeywords(string input)
    {
        // Remove conversational filler and temporal phrases
        var cleaned = Regex.Replace(input, @"\b(?:show|find|search|what|was|the|command|i|used|from|in|for|matching|about|history|yesterday|today|last|week|month|past|days|hours|all|get|display|me|to)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"['"",?]", " ").Trim();

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        return words;
    }
}
