using System;
using System.Text.RegularExpressions;

namespace SagarShell.Engine.Utilities;

/// <summary>
/// Parses natural language temporal queries into PowerShell date/time filters.
/// </summary>
public static class NaturalTimeParser
{
    private static readonly Regex RelativePastRegex = new(
        @"(?:in\s+the\s+)?(?:last|past)\s+(?<num>\d+)?\s*(?<unit>minutes?|hours?|days?|weeks?|months?|years?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OlderThanRegex = new(
        @"(?:older\s+than|before)\s+(?<num>\d+)?\s*(?<unit>minutes?|hours?|days?|weeks?|months?|years?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TodayYesterdayRegex = new(
        @"\b(?<target>today|yesterday|this\s+week|this\s+month)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record ParsedTimeFilter(string PropertyName, string ComparisonOperator, string PowerShellExpression, string HumanSummary);

    public static ParsedTimeFilter? TryParse(string input, string defaultProperty = "LastWriteTime")
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var prop = input.Contains("created", StringComparison.OrdinalIgnoreCase) ? "CreationTime" : defaultProperty;

        // Check "older than 30 days"
        var olderMatch = OlderThanRegex.Match(input);
        if (olderMatch.Success)
        {
            var num = int.TryParse(olderMatch.Groups["num"].Value, out var n) ? n : 1;
            var unit = olderMatch.Groups["unit"].Value.ToLowerInvariant();
            var psDate = GetDateOffsetExpression(-num, unit);
            return new ParsedTimeFilter(prop, "-lt", $"$_{prop} -lt ({psDate})", $"older than {num} {unit}");
        }

        // Check "last 2 hours", "past 5 days"
        var pastMatch = RelativePastRegex.Match(input);
        if (pastMatch.Success)
        {
            var num = int.TryParse(pastMatch.Groups["num"].Value, out var n) ? n : 1;
            var unit = pastMatch.Groups["unit"].Value.ToLowerInvariant();
            var psDate = GetDateOffsetExpression(-num, unit);
            return new ParsedTimeFilter(prop, "-ge", $"$_{prop} -ge ({psDate})", $"in the last {num} {unit}");
        }

        // Check "today", "yesterday", "this week"
        var specMatch = TodayYesterdayRegex.Match(input);
        if (specMatch.Success)
        {
            var target = specMatch.Groups["target"].Value.ToLowerInvariant();
            return target switch
            {
                "today" => new ParsedTimeFilter(prop, "-ge", $"$_{prop} -ge (Get-Date).Date", "modified today"),
                "yesterday" => new ParsedTimeFilter(prop, "-ge", $"$_{prop} -ge (Get-Date).Date.AddDays(-1) -and $_{prop} -lt (Get-Date).Date", "modified yesterday"),
                "this week" => new ParsedTimeFilter(prop, "-ge", $"$_{prop} -ge (Get-Date).Date.AddDays(-(Get-Date).DayOfWeek)", "modified this week"),
                "this month" => new ParsedTimeFilter(prop, "-ge", $"$_{prop} -ge (Get-Date).Date.AddDays(-(Get-Date).Day + 1)", "modified this month"),
                _ => null
            };
        }

        return null;
    }

    private static string GetDateOffsetExpression(int negativeAmount, string unit)
    {
        return unit switch
        {
            var u when u.StartsWith("minute") => $"(Get-Date).AddMinutes({negativeAmount})",
            var u when u.StartsWith("hour") => $"(Get-Date).AddHours({negativeAmount})",
            var u when u.StartsWith("day") => $"(Get-Date).AddDays({negativeAmount})",
            var u when u.StartsWith("week") => $"(Get-Date).AddDays({negativeAmount * 7})",
            var u when u.StartsWith("month") => $"(Get-Date).AddMonths({negativeAmount})",
            var u when u.StartsWith("year") => $"(Get-Date).AddYears({negativeAmount})",
            _ => $"(Get-Date).AddDays({negativeAmount})"
        };
    }
}
