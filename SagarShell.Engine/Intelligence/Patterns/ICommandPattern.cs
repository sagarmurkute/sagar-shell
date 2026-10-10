using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Interface for domain-specific deterministic natural language pattern matchers.
/// </summary>
public interface ICommandPattern
{
    /// <summary>
    /// Higher priority patterns are evaluated first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Checks if this pattern matcher can handle the given input.
    /// </summary>
    bool CanHandle(string input, ShellContext? context);

    /// <summary>
    /// Translates the input into a structured TranslationResult.
    /// </summary>
    TranslationResult Translate(string input, ShellContext? context);
}
