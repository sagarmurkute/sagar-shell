using System;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// A selectable option presented to the user when resolving an ambiguous natural language request.
/// </summary>
public class ClarificationOption
{
    public int Index { get; set; }
    public required string Label { get; set; }
    public required string Description { get; set; }
    public required string PowerShellScript { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public IntentCategory Category { get; set; } = IntentCategory.GeneralQuery;
    public bool RequiresElevation { get; set; }

    public TranslationResult ToTranslationResult(string originalInput)
    {
        return TranslationResult.CreateSuccess(
            originalInput,
            PowerShellScript,
            Description,
            Risk,
            Category
        );
    }
}
