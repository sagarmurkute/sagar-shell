using System.Diagnostics.CodeAnalysis;

namespace SagarShell.Engine.Models;

/// <summary>
/// Encapsulates a natural language input query and its runtime environment context.
/// </summary>
public class TranslationRequest
{
    public string NaturalLanguageInput { get; set; } = string.Empty;
    public ShellContext? Context { get; set; }
    public bool EnableDryRun { get; set; }
    public bool PreferNativeAction { get; set; } = false;

    public TranslationRequest() { }

    [SetsRequiredMembers]
    public TranslationRequest(string input, ShellContext? context = null)
    {
        NaturalLanguageInput = input;
        Context = context;
    }
}
