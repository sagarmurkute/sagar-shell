using System;
using System.Collections.Generic;

namespace SagarShell.Engine.Models;

/// <summary>
/// Result of translating natural English into a deterministic Windows operation.
/// </summary>
public class TranslationResult
{
    public string OriginalInput { get; set; } = string.Empty;
    public string PowerShellScript { get; set; } = string.Empty;
    public string? AlternativeCmdScript { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public bool RequiresElevation { get; set; } = false;
    public double Confidence { get; set; } = 1.0;
    public IntentCategory Category { get; set; } = IntentCategory.GeneralQuery;
    public ExtractedIntent? Intent { get; set; }
    public ExtractedEntityBag? ExtractedEntities { get; set; } = new();
    public List<string> SuggestedFollowUps { get; set; } = new();
    public string? RiskExplanation { get; set; }
    public bool IsDryRun { get; set; }

    /// <summary>
    /// Clarification request when intent is ambiguous or requires user disambiguation.
    /// </summary>
    public SagarShell.Engine.Intelligence.ClarificationRequest? Clarification { get; set; }
    public bool NeedsClarification => Clarification != null;

    /// <summary>
    /// Direct in-memory C# action delegate for zero-latency execution if applicable.
    /// </summary>
    public Func<ShellContext, ExecutionResult>? DirectNativeAction { get; set; }

    public static TranslationResult CreateSuccess(
        string originalInput,
        string powerShellScript,
        string explanation,
        RiskLevel risk = RiskLevel.Safe,
        IntentCategory category = IntentCategory.GeneralQuery,
        double confidence = 1.0)
    {
        return new TranslationResult
        {
            OriginalInput = originalInput,
            PowerShellScript = powerShellScript,
            Explanation = explanation,
            Risk = risk,
            Category = category,
            Confidence = confidence
        };
    }

    public static TranslationResult CreateUnknown(string originalInput, string reason = "Could not unambiguously translate instruction into a Windows command.")
    {
        return new TranslationResult
        {
            OriginalInput = originalInput,
            PowerShellScript = string.Empty,
            Explanation = reason,
            Risk = RiskLevel.Safe,
            Confidence = 0.0,
            Category = IntentCategory.GeneralQuery
        };
    }
}
