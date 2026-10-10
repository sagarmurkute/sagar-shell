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
    /// Indicates whether the response is conversational (e.g. proposing an action, explaining results, or asking for input).
    /// </summary>
    public bool IsConversationalResponse { get; set; }

    /// <summary>
    /// Conversational message or question to display directly to the user.
    /// </summary>
    public string? ConversationalMessage { get; set; }

    /// <summary>
    /// Indicates whether a proposed action is waiting for user affirmation ("Yes" / "Do it").
    /// </summary>
    public bool IsPendingProposal => PendingProposal != null;

    /// <summary>
    /// The action proposed by the assistant awaiting user confirmation.
    /// </summary>
    public TranslationResult? PendingProposal { get; set; }

    /// <summary>
    /// When true, explicit confirmation with impact explanation is required before execution.
    /// </summary>
    public bool RequiresExplicitConfirmation { get; set; }

    /// <summary>
    /// Plain-English explanation of real-world system impact (e.g. "Will terminate Chrome PID 14200 closing 12 tabs").
    /// </summary>
    public string? ImpactExplanation { get; set; }

    /// <summary>
    /// Direct in-memory C# action delegate for zero-latency execution if applicable.
    /// </summary>
    public Func<ShellContext, ExecutionResult>? DirectNativeAction { get; set; }

    public static TranslationResult CreateConversational(
        string originalInput, 
        string message, 
        TranslationResult? proposedAction = null,
        IntentCategory category = IntentCategory.GeneralQuery)
    {
        return new TranslationResult
        {
            OriginalInput = originalInput,
            PowerShellScript = proposedAction?.PowerShellScript ?? string.Empty,
            Explanation = message,
            ConversationalMessage = message,
            IsConversationalResponse = true,
            PendingProposal = proposedAction,
            Category = category,
            Risk = proposedAction?.Risk ?? RiskLevel.Safe,
            Confidence = 1.0
        };
    }

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
