using System;
using System.Linq;
using System.Management.Automation.Language;
using System.Text.Json;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Validates and sanitizes raw AI / LLM generated interpretations into structured, safe PowerShell operations.
/// Prevents execution of malformed outputs, syntax errors, and hallucinated commands.
/// </summary>
public static class AIOutputValidator
{
    private static readonly Regex JsonCodeBlockRegex = new(
        @"```(?:json)?\s*([\s\S]*?)\s*```",
        RegexOptions.Compiled);

    private static readonly Regex DestructiveRootRegex = new(
        @"\b(?:rm\s+-rf\s+/\b|format\s+[a-zA-Z]:|del(?:\s+/[a-zA-Z]+)*\s+[a-zA-Z]:\\windows|remove-item\s+.*(?:[a-zA-Z]:\\(?:windows|system32)|/system32))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public record AIValidationResult(
        bool IsValid, 
        TranslationResult? ValidatedTranslation, 
        string? ErrorReason = null);

    public static AIValidationResult ValidateTranslation(TranslationResult translation, ShellContext? context = null)
    {
        if (translation == null || string.IsNullOrWhiteSpace(translation.PowerShellScript))
        {
            return new AIValidationResult(false, null, "AI translation was empty or null.");
        }

        var script = translation.PowerShellScript;

        // 1. Syntax Validation via PowerShell AST Parser
        Parser.ParseInput(script, out Token[] tokens, out ParseError[] parseErrors);
        if (parseErrors != null && parseErrors.Length > 0)
        {
            var errorDetails = string.Join("; ", parseErrors.Select(p => p.Message));
            return new AIValidationResult(false, null, $"PowerShell syntax error in AI generated script: {errorDetails}");
        }

        // 2. Destructive Root Checks
        if (DestructiveRootRegex.IsMatch(script))
        {
            return new AIValidationResult(false, null, "AI proposed an unsafe destructive operation targeting root system directories.");
        }

        // 3. Safety & Elevation Analysis
        var safety = SafetyAnalyzer.Analyze(script, translation.Category);
        translation.Risk = safety.Level;
        translation.RequiresElevation = safety.RequiresElevation;
        translation.RiskExplanation = safety.Explanation;

        return new AIValidationResult(true, translation);
    }

    public static AIValidationResult ValidateAndParse(string rawAiResponse, string originalInput, ShellContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(rawAiResponse))
        {
            return new AIValidationResult(false, null, "AI response was empty or null.");
        }

        // 1. Extract JSON (unwrap markdown code fences if present)
        var json = rawAiResponse.Trim();
        var match = JsonCodeBlockRegex.Match(json);
        if (match.Success)
        {
            json = match.Groups[1].Value.Trim();
        }

        string script;
        string explanation;
        IntentCategory category = IntentCategory.GeneralQuery;
        double confidence = 0.85;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            script = root.TryGetProperty("powerShellScript", out var s) ? s.GetString() ?? "" : "";
            explanation = root.TryGetProperty("explanation", out var e) ? e.GetString() ?? "" : "";

            if (root.TryGetProperty("category", out var c) && Enum.TryParse<IntentCategory>(c.GetString(), true, out var parsedCat))
            {
                category = parsedCat;
            }

            if (root.TryGetProperty("confidence", out var conf) && conf.TryGetDouble(out var confVal))
            {
                confidence = confVal;
            }
        }
        catch (JsonException ex)
        {
            // Fallback: If AI returned raw PowerShell script directly
            if (!json.StartsWith("{") && !json.Contains("```"))
            {
                script = json;
                explanation = $"AI generated operation for: \"{originalInput}\"";
            }
            else
            {
                return new AIValidationResult(false, null, $"Malformed AI output (Invalid JSON): {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            return new AIValidationResult(false, null, "AI generated an empty PowerShell command.");
        }

        // 2. Syntax Validation via PowerShell AST Parser
        Parser.ParseInput(script, out Token[] tokens, out ParseError[] parseErrors);
        if (parseErrors != null && parseErrors.Length > 0)
        {
            var errorDetails = string.Join("; ", parseErrors.Select(p => p.Message));
            return new AIValidationResult(false, null, $"PowerShell syntax error in AI generated script: {errorDetails}");
        }

        // 3. Destructive Root Checks
        if (DestructiveRootRegex.IsMatch(script))
        {
            return new AIValidationResult(false, null, "AI proposed an unsafe destructive operation targeting root system directories.");
        }

        // 4. Safety & Elevation Analysis
        var safety = SafetyAnalyzer.Analyze(script, category);

        var result = TranslationResult.CreateSuccess(
            originalInput,
            script,
            string.IsNullOrEmpty(explanation) ? $"Executes \"{script}\"" : explanation,
            safety.Level,
            category,
            confidence
        );

        result.RequiresElevation = safety.RequiresElevation;
        result.RiskExplanation = safety.Explanation;

        return new AIValidationResult(true, result);
    }
}
