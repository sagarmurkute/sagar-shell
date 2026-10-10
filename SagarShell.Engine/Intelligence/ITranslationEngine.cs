using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence.AIProviders;
using SagarShell.Engine.Intelligence.Patterns;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Core translation contract for converting natural language queries into executable Windows operations.
/// </summary>
public interface ITranslationEngine
{
    TranslationResult Translate(TranslationRequest request);
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
    void RegisterPattern(ICommandPattern pattern);
    void SetAIProvider(ILLMProvider provider);
}
