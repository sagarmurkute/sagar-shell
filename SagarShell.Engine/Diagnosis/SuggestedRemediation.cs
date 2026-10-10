using System;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// A validated, proposed remediation step to recover from an execution error.
/// </summary>
public class SuggestedRemediation
{
    public required string Description { get; set; }
    public string? RemediationScript { get; set; }
    public string? RetryScript { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public bool RequiresElevation { get; set; } = false;
    public bool RequiresApproval { get; set; } = true;
    public bool CanAutoRetryAfterFix => !string.IsNullOrEmpty(RetryScript);

    /// <summary>
    /// Optional pre-validation delegate that verifies the proposed fix is valid and safe before asking for approval.
    /// </summary>
    public Func<ShellContext, Task<RemediationValidationReport>>? PreValidator { get; set; }
}

public record RemediationValidationReport(bool IsValid, string? Reason = null);
