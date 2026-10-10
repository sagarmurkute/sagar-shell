using System;
using System.Collections.Generic;
using System.Linq;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Planning;

public enum PlanStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// A directed sequence of dependent tasks generated from a compound natural language instruction.
/// </summary>
public class ExecutionPlan
{
    public required string OriginalInstruction { get; set; }
    public List<TaskStep> Steps { get; set; } = new();
    public PlanStatus Status { get; set; } = PlanStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int TotalSteps => Steps.Count;
    public bool HasSteps => Steps.Count > 0;
    
    public RiskLevel OverallRisk => Steps.Count > 0 
        ? (RiskLevel)Steps.Max(s => (int)s.Risk) 
        : RiskLevel.Safe;

    public bool RequiresElevation => Steps.Any(s => s.RequiresElevation);
    public bool RequiresConfirmation => Steps.Any(s => s.RequiresConfirmation || s.Risk >= RiskLevel.Moderate);

    public TaskStep? GetStep(int index) => Steps.FirstOrDefault(s => s.StepIndex == index);

    public override string ToString()
    {
        var lines = new List<string>
        {
            $"Execution Plan for: \"{OriginalInstruction}\" [{TotalSteps} Steps | Overall Risk: {OverallRisk}]",
            new string('=', 65)
        };

        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            var deps = s.DependsOnStepIndices.Count > 0
                ? $" (Depends on Step {string.Join(", ", s.DependsOnStepIndices.Select(d => d + 1))})"
                : "";
            lines.Add($"[{i + 1}] [{s.Risk}] {s.Description}{deps}");
            lines.Add($"    Script: {s.Translation.PowerShellScript}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
