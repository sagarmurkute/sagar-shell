using System;
using System.Collections.Generic;
using System.Linq;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// A clarifying question with structured options generated when a command is ambiguous.
/// </summary>
public class ClarificationRequest
{
    public required string OriginalInput { get; set; }
    public required string Question { get; set; }
    public List<ClarificationOption> Options { get; set; } = new();

    public ClarificationOption? GetOption(int index) =>
        Options.FirstOrDefault(o => o.Index == index);

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
