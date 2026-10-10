using System;
using System.Text;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// A structured, plain-English diagnosis of an execution failure with likely causes and actionable safe fixes.
/// </summary>
public class ErrorDiagnosis
{
    public ErrorCategory Category { get; set; } = ErrorCategory.Unknown;
    public string RawErrorMessage { get; set; } = string.Empty;
    public string PlainEnglishSummary { get; set; } = string.Empty;
    public string LikelyCause { get; set; } = string.Empty;
    public SuggestedRemediation? SuggestedFix { get; set; }
    public string? TechnicalDetails { get; set; }
    public bool CanBeRemediated => SuggestedFix != null;

    public string FormatUserReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🔍 [Error Diagnosis: {Category}]");
        sb.AppendLine($"   Summary : {PlainEnglishSummary}");
        sb.AppendLine($"   Cause   : {LikelyCause}");

        if (SuggestedFix != null)
        {
            sb.AppendLine($"   Fix     : {SuggestedFix.Description}");
            if (!string.IsNullOrEmpty(SuggestedFix.RemediationScript))
            {
                sb.AppendLine($"   Script  : {SuggestedFix.RemediationScript}");
            }
            if (SuggestedFix.RequiresElevation)
            {
                sb.AppendLine("   Note    : This remediation requires Administrator elevation.");
            }
        }
        else
        {
            sb.AppendLine("   Fix     : No automated remediation available. Please inspect the command parameters.");
        }

        return sb.ToString().TrimEnd();
    }
}
