using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence.AIProviders;
using SagarShell.Engine.Intelligence.Patterns;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Master Natural Language Translation Engine.
/// Coordinates multi-tier parsing, pattern recognition, pronoun/context resolution, AI fallback, and safety analysis.
/// </summary>
public class NaturalLanguageEngine : ITranslationEngine
{
    private readonly List<ICommandPattern> _patterns = new();
    private ILLMProvider _aiProvider;
    private readonly ILLMProvider _fallbackProvider;

    public IReadOnlyList<ICommandPattern> RegisteredPatterns => _patterns.AsReadOnly();
    public ILLMProvider CurrentAIProvider => _aiProvider;

    public NaturalLanguageEngine(ILLMProvider? customAIProvider = null)
    {
        _fallbackProvider = new RuleBasedFallbackProvider();
        _aiProvider = customAIProvider ?? _fallbackProvider;

        RegisterDefaultPatterns();
    }

    private void RegisterDefaultPatterns()
    {
        RegisterPattern(new HelpPatterns());
        RegisterPattern(new HistoryPatterns());
        RegisterPattern(new NavigationPatterns());
        RegisterPattern(new FileSystemPatterns());
        RegisterPattern(new ProcessPatterns());
        RegisterPattern(new WindowsServicePatterns());
        RegisterPattern(new NetworkPatterns());
        RegisterPattern(new SystemPatterns());
        RegisterPattern(new GitPatterns());
        RegisterPattern(new WindowAndAppPatterns());
    }

    public void RegisterPattern(ICommandPattern pattern)
    {
        _patterns.Add(pattern);
        // Keep sorted by Priority descending
        _patterns.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    public void SetAIProvider(ILLMProvider provider)
    {
        _aiProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public TranslationResult Translate(TranslationRequest request)
    {
        return TranslateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.NaturalLanguageInput))
        {
            return TranslationResult.CreateUnknown(request?.NaturalLanguageInput ?? string.Empty, "Input cannot be empty.");
        }

        var rawInput = request.NaturalLanguageInput;
        var sanitized = NaturalTextSanitizer.Sanitize(rawInput);
        var context = request.Context ?? new ShellContext();

        // 1. Context-Aware Multi-Turn Conversational Dialogue Reasoning
        if (SagarShell.Engine.Intelligence.Conversation.ConversationalDialogueEngine.IsConversationalTurn(sanitized, context))
        {
            var convResult = SagarShell.Engine.Intelligence.Conversation.ConversationalDialogueEngine.TryProcessConversation(sanitized, context);
            if (convResult != null)
            {
                EnrichAndAnalyzeSafety(convResult, request);
                return convResult;
            }
        }

        // 2. AI / LLM Provider Translation with Conversational Context & AST Validation
        if (_aiProvider.IsAvailable && _aiProvider != _fallbackProvider)
        {
            var aiResult = await _aiProvider.TranslateAsync(sanitized, context, cancellationToken);
            if (aiResult != null && (!string.IsNullOrWhiteSpace(aiResult.PowerShellScript) || aiResult.IsConversationalResponse))
            {
                if (!string.IsNullOrWhiteSpace(aiResult.PowerShellScript))
                {
                    var validation = AIOutputValidator.ValidateTranslation(aiResult, context);
                    if (validation.IsValid && validation.ValidatedTranslation != null)
                    {
                        EnrichAndAnalyzeSafety(validation.ValidatedTranslation, request);
                        return validation.ValidatedTranslation;
                    }
                }
                else
                {
                    return aiResult;
                }
            }
        }

        // 3. Check Ambiguity and Intent Clarification
        if (AmbiguityDetector.IsAmbiguous(sanitized, context))
        {
            var clarification = AmbiguityDetector.DetectAmbiguity(sanitized, context);
            if (clarification != null)
            {
                var ambigResult = TranslationResult.CreateSuccess(
                    rawInput,
                    "",
                    clarification.Question,
                    RiskLevel.Safe,
                    IntentCategory.GeneralQuery
                );
                ambigResult.Clarification = clarification;
                return ambigResult;
            }
        }

        // 4. Check Pronouns and Pipeline Continuations if context has prior history
        if (PronounResolver.ContainsPronounOrContinuation(sanitized) && context.History.Count > 0)
        {
            var followUpResult = PronounResolver.TryResolveFollowUp(sanitized, context);
            if (followUpResult != null)
            {
                EnrichAndAnalyzeSafety(followUpResult, request);
                return followUpResult;
            }
        }

        // 5. High-speed Deterministic Pattern Matchers
        foreach (var pattern in _patterns)
        {
            if (pattern.CanHandle(sanitized, context))
            {
                var result = pattern.Translate(sanitized, context);
                EnrichAndAnalyzeSafety(result, request);
                return result;
            }
        }

        // 6. Rule-based Fallback Slot Resolver
        var fallbackResult = await _fallbackProvider.TranslateAsync(sanitized, context, cancellationToken);
        if (fallbackResult != null && !string.IsNullOrWhiteSpace(fallbackResult.PowerShellScript))
        {
            EnrichAndAnalyzeSafety(fallbackResult, request);
            return fallbackResult;
        }

        // 7. Unknown
        return TranslationResult.CreateUnknown(rawInput, $"Unable to unambiguously resolve '{rawInput}' into a safe Windows command.");
    }

    private static void EnrichAndAnalyzeSafety(TranslationResult result, TranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(result.PowerShellScript))
            return;

        // Run safety analysis
        var riskAnalysis = SafetyAnalyzer.Analyze(result.PowerShellScript, result.Category);
        result.Risk = riskAnalysis.Level;
        result.RequiresElevation = riskAnalysis.RequiresElevation;
        result.RiskExplanation = riskAnalysis.Explanation;

        // Handle DryRun flag
        if (request.EnableDryRun && riskAnalysis.suggestedWhatIf != null)
        {
            result.PowerShellScript = riskAnalysis.suggestedWhatIf;
            result.IsDryRun = true;
            result.Explanation = $"[Dry Run Preview] {result.Explanation}";
        }
    }
}
