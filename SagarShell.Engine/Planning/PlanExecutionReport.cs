using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SagarShell.Engine.Planning;

/// <summary>
/// Detailed execution audit report of an executed multi-step plan.
/// </summary>
public class PlanExecutionReport
{
    public required ExecutionPlan Plan { get; set; }
    public bool Success { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int SucceededCount => Plan.Steps.Count(s => s.Status == StepStatus.Succeeded);
    public int FailedCount => Plan.Steps.Count(s => s.Status == StepStatus.Failed);
    public int SkippedCount => Plan.Steps.Count(s => s.Status == StepStatus.Skipped);

    public TaskStep? FailedStep => Plan.Steps.FirstOrDefault(s => s.Status == StepStatus.Failed);
    public string? AbortReason { get; set; }

    public string GenerateSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Plan Execution: {(Success ? "SUCCESS" : "FAILED")} in {TotalDuration.TotalMilliseconds:F0} ms");
        sb.AppendLine($"Steps Succeeded: {SucceededCount}/{Plan.TotalSteps} | Failed: {FailedCount} | Skipped: {SkippedCount}");
        sb.AppendLine(new string('-', 60));

        foreach (var step in Plan.Steps)
        {
            var statusBadge = step.Status switch
            {
                StepStatus.Succeeded => "[✓ SUCCEEDED]",
                StepStatus.Failed => "[✗ FAILED]",
                StepStatus.Skipped => "[↷ SKIPPED]",
                StepStatus.Aborted => "[⛔ ABORTED]",
                _ => "[? PENDING]"
            };

            sb.AppendLine($"{statusBadge} Step {step.StepIndex + 1}: {step.Description}");
            if (step.Status == StepStatus.Failed && !string.IsNullOrEmpty(step.FailureReason))
            {
                sb.AppendLine($"    Error: {step.FailureReason}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
