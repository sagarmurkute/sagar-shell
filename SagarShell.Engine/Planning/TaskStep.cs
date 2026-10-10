using System;
using System.Collections.Generic;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Planning;

public enum StepStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    Skipped,
    Aborted
}

/// <summary>
/// Represents a single atomic step within a multi-step execution plan.
/// </summary>
public class TaskStep
{
    public int StepIndex { get; set; }
    public required string RawInstruction { get; set; }
    public required TranslationResult Translation { get; set; }
    public string Description => Translation.Explanation;
    public RiskLevel Risk => Translation.Risk;
    public bool RequiresElevation => Translation.RequiresElevation;
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// Indices of steps that must succeed before this step can execute.
    /// </summary>
    public List<int> DependsOnStepIndices { get; set; } = new();

    public StepStatus Status { get; set; } = StepStatus.Pending;
    public ExecutionResult? Result { get; set; }
    public TimeSpan Duration { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// Optional pre-execution validation rule.
    /// </summary>
    public Func<ShellContext, TaskStep, StepValidationResult>? Validator { get; set; }
}

public record StepValidationResult(bool IsValid, string? ErrorMessage = null);
