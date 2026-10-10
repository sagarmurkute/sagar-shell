using System;
using System.Collections.Generic;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Memory;

/// <summary>
/// A persisted, sanitized record of a single executed natural language command.
/// </summary>
public class HistoricalCommandRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string RawInput { get; set; }
    public required string SanitizedInput { get; set; }
    public required string PowerShellScript { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public IntentCategory Category { get; set; } = IntentCategory.GeneralQuery;
    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public bool Success { get; set; } = true;
    public int ExitCode { get; set; }
    public TimeSpan Duration { get; set; }
    public string? OutputSummary { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public List<string> ExtractedPaths { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    public override string ToString()
    {
        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] \"{RawInput}\" -> {PowerShellScript} ({(Success ? "OK" : "FAILED")})";
    }
}
