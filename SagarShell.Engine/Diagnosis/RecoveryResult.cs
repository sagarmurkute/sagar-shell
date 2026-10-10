using System;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// Result of an interactive error recovery attempt.
/// </summary>
public class RecoveryResult
{
    public bool ValidationPassed { get; set; }
    public string? ValidationFailureReason { get; set; }
    public bool ApprovalGranted { get; set; }
    public bool RemediationAttempted { get; set; }
    public bool RemediationSucceeded { get; set; }
    public ExecutionResult? RemediationOutput { get; set; }
    public bool RetryAttempted { get; set; }
    public bool RetrySucceeded { get; set; }
    public ExecutionResult? RetryOutput { get; set; }
    public string SummaryMessage { get; set; } = string.Empty;
}
