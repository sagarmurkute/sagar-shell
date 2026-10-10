using System.Collections.Generic;

namespace SagarShell.Engine.Models;

/// <summary>
/// Structured semantic representation of the user's natural language request.
/// </summary>
public class ExtractedIntent
{
    public required string RawQuery { get; set; }
    public required string NormalizedQuery { get; set; }
    public IntentCategory Category { get; set; } = IntentCategory.GeneralQuery;
    public string ActionVerb { get; set; } = string.Empty;
    public string TargetNoun { get; set; } = string.Empty;
    public Dictionary<string, string> Slots { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public bool IsFollowUp { get; set; }
    public string? PronounReference { get; set; }
    public double Confidence { get; set; } = 1.0;

    public string? GetSlot(string key) => Slots.TryGetValue(key, out var val) ? val : null;
    
    public void SetSlot(string key, string value) => Slots[key] = value;

    public bool HasSlot(string key) => Slots.ContainsKey(key);
}
