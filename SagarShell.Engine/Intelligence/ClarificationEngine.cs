using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Intelligence.Conversation;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Master engine for confidence calibration, ambiguity detection, and multi-turn clarification resolution.
/// Ensures Sagar Shell asks clarifying questions instead of guessing or executing wrong commands.
/// </summary>
public static class ClarificationEngine
{
    private static readonly Regex CancellationRegex = new(
        @"^(?:cancel|never\s+mind|abort|neither|stop|none|n|no\s+thanks|skip)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks if the input is a response to an ongoing clarification session.
    /// </summary>
    public static bool IsClarificationResponse(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input) || context.ActiveClarification == null)
            return false;

        return true;
    }

    /// <summary>
    /// Resolves a user's answer to an active clarification request.
    /// </summary>
    public static TranslationResult ResolveClarification(string input, ShellContext context)
    {
        var clar = context.ActiveClarification;
        if (clar == null)
        {
            return TranslationResult.CreateUnknown(input, "No active clarification in progress.");
        }

        var clean = NaturalTextSanitizer.Sanitize(input).Trim();

        // 1. Check for cancellation
        if (CancellationRegex.IsMatch(clean))
        {
            context.ActiveClarification = null;
            return TranslationResult.CreateConversational(
                input,
                "Understood. Clarification cancelled. What would you like to do instead?",
                null,
                IntentCategory.GeneralQuery
            );
        }

        // 2. Check if user provided a refined semantic instruction (e.g. "actually in folder D:\logs", "only pdf files in downloads", "kill chrome instead")
        if (clean.StartsWith("actually", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("no", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("only", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("just", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("in folder") ||
            clean.Contains("files in") ||
            clean.Contains("instead"))
        {
            var semanticIntent = SemanticIntentParser.Parse(clean, context);
            if (semanticIntent != null && semanticIntent.TargetType != SemanticIntentParser.EntityType.Unknown)
            {
                if (clar.DisambiguationType == "DeleteFiles" && semanticIntent.Action == SemanticIntentParser.SemanticAction.FindOrList)
                {
                    semanticIntent.Action = SemanticIntentParser.SemanticAction.RemoveOrDelete;
                }
                else if (clar.DisambiguationType == "ProcessAction" && semanticIntent.Action == SemanticIntentParser.SemanticAction.FindOrList)
                {
                    semanticIntent.Action = SemanticIntentParser.SemanticAction.TerminateOrKill;
                }

                var semanticResult = SemanticIntentParser.GenerateTranslation(semanticIntent, input, context);
                if (semanticResult != null && !string.IsNullOrWhiteSpace(semanticResult.PowerShellScript))
                {
                    context.ActiveClarification = null; // Consume clarification
                    semanticResult.ConversationalMessage = $"Understood: {semanticResult.Explanation}";
                    semanticResult.IsConversationalResponse = true;
                    return semanticResult;
                }
            }
        }

        // 3. Try to match an existing option by index number, ordinal, or keyword
        var matchedOption = clar.FindOptionByQuery(clean);
        if (matchedOption != null)
        {
            context.ActiveClarification = null; // Consume clarification

            var result = TranslationResult.CreateSuccess(
                input,
                matchedOption.PowerShellScript,
                matchedOption.Description,
                matchedOption.Risk,
                matchedOption.Category,
                confidence: 0.95
            );
            result.RequiresElevation = matchedOption.RequiresElevation;
            result.ConversationalMessage = $"Executing [{matchedOption.Index}] {matchedOption.Label}...";
            result.IsConversationalResponse = true;
            return result;
        }

        // 4. Fallback semantic parsing check
        var fallbackSemantic = SemanticIntentParser.Parse(clean, context);
        if (fallbackSemantic != null && fallbackSemantic.TargetType != SemanticIntentParser.EntityType.Unknown)
        {
            if (clar.DisambiguationType == "DeleteFiles" && fallbackSemantic.Action == SemanticIntentParser.SemanticAction.FindOrList)
            {
                fallbackSemantic.Action = SemanticIntentParser.SemanticAction.RemoveOrDelete;
            }

            var semanticResult = SemanticIntentParser.GenerateTranslation(fallbackSemantic, input, context);
            if (semanticResult != null && !string.IsNullOrWhiteSpace(semanticResult.PowerShellScript))
            {
                context.ActiveClarification = null;
                semanticResult.ConversationalMessage = $"Understood: {semanticResult.Explanation}";
                semanticResult.IsConversationalResponse = true;
                return semanticResult;
            }
        }

        // 4. If neither matched, ask user gently to pick one of the options
        var followUp = TranslationResult.CreateSuccess(
            input,
            "",
            $"Please choose an option between 1 and {clar.Options.Count}, or type 'cancel'.",
            RiskLevel.Safe,
            IntentCategory.GeneralQuery
        );
        followUp.Clarification = clar;
        followUp.IsConversationalResponse = true;
        followUp.ConversationalMessage = $"I didn't quite catch that. Please select an option (1-{clar.Options.Count}) or type 'cancel'.";
        return followUp;
    }

    /// <summary>
    /// Calibrates confidence score for a proposed translation and determines if clarification is necessary.
    /// </summary>
    public static bool ShouldClarify(string input, TranslationResult? result, ShellContext context)
    {
        if (result == null) return true;

        // If explicitly ambiguous
        if (AmbiguityDetector.IsAmbiguous(input, context)) return true;

        // If confidence is below the context threshold
        if (result.Confidence < context.ConfidenceThreshold) return true;

        return false;
    }
}
