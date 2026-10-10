using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.AIProviders;

/// <summary>
/// Interface for AI/LLM translation providers for translating complex or novel natural language shell queries.
/// </summary>
public interface ILLMProvider
{
    string ProviderName { get; }
    bool IsAvailable { get; }

    Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, CancellationToken cancellationToken = default);
}
