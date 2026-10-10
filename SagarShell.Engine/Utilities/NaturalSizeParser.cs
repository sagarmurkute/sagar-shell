using System;
using System.Text.RegularExpressions;

namespace SagarShell.Engine.Utilities;

/// <summary>
/// Parses natural language file size expressions into PowerShell size filters and byte values.
/// </summary>
public static class NaturalSizeParser
{
    private static readonly Regex SizePattern = new(
        @"(?<comparison>larger\s+than|greater\s+than|more\s+than|over|bigger\s+than|at\s+least|above|>=|>|smaller\s+than|less\s+than|under|below|at\s+most|<=|<|equal\s+to|exactly|==|=)?\s*(?<num>\d+(?:\.\d+)?)\s*(?<unit>bytes?|b|kb|kilo(?:bytes?)?|mb|mega(?:bytes?)?|gb|giga(?:bytes?)?|tb|tera(?:bytes?)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmptyPattern = new(
        @"\b(?:empty|zero\s+bytes?|0\s*b(?:ytes?)?|empty\s+files?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record ParsedSize(long Bytes, string ComparisonOperator, string PowerShellFilter, string PowerShellUnit);

    public static ParsedSize? TryParse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (EmptyPattern.IsMatch(input))
        {
            return new ParsedSize(0, "-eq", "$_.Length -eq 0", "0");
        }

        var match = SizePattern.Match(input);
        if (!match.Success)
            return null;

        var compStr = match.Groups["comparison"].Value.ToLowerInvariant().Trim();
        var numStr = match.Groups["num"].Value;
        var unitStr = match.Groups["unit"].Value.ToLowerInvariant();

        if (!double.TryParse(numStr, out var num))
            return null;

        var (multiplier, psUnit) = unitStr switch
        {
            "b" or "byte" or "bytes" => (1L, ""),
            "kb" or "kilo" or "kilobyte" or "kilobytes" => (1024L, "KB"),
            "mb" or "mega" or "megabyte" or "megabytes" => (1024L * 1024L, "MB"),
            "gb" or "giga" or "gigabyte" or "gigabytes" => (1024L * 1024L * 1024L, "GB"),
            "tb" or "tera" or "terabyte" or "terabytes" => (1024L * 1024L * 1024L * 1024L, "TB"),
            _ => (1024L * 1024L, "MB")
        };

        long totalBytes = (long)(num * multiplier);

        string op = compStr switch
        {
            "smaller than" or "less than" or "under" or "below" or "<" => "-lt",
            "at most" or "<=" => "-le",
            "at least" or ">=" => "-ge",
            "equal to" or "exactly" or "==" or "=" => "-eq",
            _ => "-gt" // default: larger than, greater than, over, etc.
        };

        string psExpression = string.IsNullOrEmpty(psUnit)
            ? $"$_.Length {op} {numStr}"
            : $"$_.Length {op} {numStr}{psUnit}";

        return new ParsedSize(totalBytes, op, psExpression, $"{numStr}{psUnit}");
    }
}
