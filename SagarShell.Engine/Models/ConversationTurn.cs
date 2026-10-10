using System;
using System.Collections.Generic;

namespace SagarShell.Engine.Models;

/// <summary>
/// Represents a single conversational turn in a multi-turn dialogue session.
/// Preserves user utterances, assistant explanations, proposed actions, and execution outcomes.
/// </summary>
public class ConversationTurn
{
    public int TurnIndex { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string UserMessage { get; set; } = string.Empty;
    public string AssistantResponse { get; set; } = string.Empty;
    public string? IntentGoal { get; set; }
    public TranslationResult? Translation { get; set; }
    public ExecutionResult? Execution { get; set; }
    public Dictionary<string, string> ContextEntities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasExecutedCommand => Execution != null && !string.IsNullOrEmpty(Translation?.PowerShellScript);
}
