using System;
using System.Collections.Generic;

namespace SagarShell.Engine.Memory;

/// <summary>
/// Snapshot of a complete interactive Sagar Shell session.
/// </summary>
public class SessionSnapshot
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public string InitialDirectory { get; set; } = string.Empty;
    public string FinalDirectory { get; set; } = string.Empty;
    public bool IsElevated { get; set; }
    public List<HistoricalCommandRecord> Commands { get; set; } = new();

    public int TotalCommands => Commands.Count;
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}
