using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;

namespace SagarShell.Engine.Models;

/// <summary>
/// Maintains active session state, directory context, conversation history, and entity memory for follow-up resolutions.
/// </summary>
public class ShellContext
{
    private string _currentDirectory;

    public string CurrentDirectory
    {
        get => _currentDirectory;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                PreviousDirectory = _currentDirectory;
                _currentDirectory = Path.GetFullPath(value);
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                PreviousDirectory = _currentDirectory;
                _currentDirectory = value;
            }
        }
    }

    public string? PreviousDirectory { get; set; }

    public bool IsElevated { get; set; }

    public List<CommandHistoryEntry> History { get; set; } = new();

    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Remembers the last targeted entities (e.g. process names, PIDs, file paths, ports) for pronoun resolution ("it", "them", "that").
    /// </summary>
    public EntityMemory Memory { get; set; } = new();

    public ShellContext(string? initialDirectory = null)
    {
        _currentDirectory = initialDirectory ?? Directory.GetCurrentDirectory();
        IsElevated = CheckIsElevated();
    }

    private static bool CheckIsElevated()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            // Ignore elevation check failure
        }
        return false;
    }

    /// <summary>
    /// Preserved native PSObjects from the most recent command execution for in-memory filtering, sorting, and export.
    /// </summary>
    public List<System.Management.Automation.PSObject> LastOutputObjects { get; set; } = new();

    /// <summary>
    /// Multi-turn conversation turns preserved within the active session.
    /// </summary>
    public List<ConversationTurn> ConversationTurns { get; set; } = new();

    /// <summary>
    /// The pending proposed action waiting for user affirmation ("Yes" / "Do it").
    /// </summary>
    public TranslationResult? ActiveProposal { get; set; }

    /// <summary>
    /// Active high-level diagnostic or operational goal (e.g. "Diagnosing system slowdown").
    /// </summary>
    public string? ActiveGoal { get; set; }

    public void AddHistory(string rawInput, TranslationResult translation, ExecutionResult? execution = null)
    {
        History.Add(new CommandHistoryEntry
        {
            Timestamp = DateTime.UtcNow,
            Input = rawInput,
            Translation = translation,
            Execution = execution
        });

        // Record conversational turn
        RecordTurn(rawInput, translation, execution);

        // Update memory
        Memory.UpdateFrom(translation, execution);

        // Update structured object pipeline cache
        if (execution?.StructuredObjects != null && execution.StructuredObjects.Count > 0)
        {
            LastOutputObjects = execution.StructuredObjects
                .OfType<System.Management.Automation.PSObject>()
                .ToList();
        }
    }

    public void RecordTurn(string userMessage, TranslationResult translation, ExecutionResult? execution = null)
    {
        var turn = new ConversationTurn
        {
            TurnIndex = ConversationTurns.Count + 1,
            Timestamp = DateTime.UtcNow,
            UserMessage = userMessage,
            AssistantResponse = translation.ConversationalMessage ?? translation.Explanation,
            IntentGoal = ActiveGoal,
            Translation = translation,
            Execution = execution
        };

        ConversationTurns.Add(turn);
    }
}

public class CommandHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Input { get; set; }
    public required TranslationResult Translation { get; set; }
    public ExecutionResult? Execution { get; set; }
}

/// <summary>
/// Short-term entity memory for resolving follow-ups.
/// </summary>
public class EntityMemory
{
    public List<string> LastReferencedPaths { get; set; } = new();
    public List<int> LastReferencedProcessIds { get; set; } = new();
    public List<string> LastReferencedProcessNames { get; set; } = new();
    public List<int> LastReferencedPorts { get; set; } = new();
    public List<string> LastReferencedServices { get; set; } = new();
    public string? LastReferencedUrlOrHost { get; set; }
    public string? LastReferencedBranch { get; set; }
    public string? LastFilterPattern { get; set; }
    public string? LastSearchTerm { get; set; }

    public void Clear()
    {
        LastReferencedPaths.Clear();
        LastReferencedProcessIds.Clear();
        LastReferencedProcessNames.Clear();
        LastReferencedPorts.Clear();
        LastReferencedServices.Clear();
        LastReferencedUrlOrHost = null;
        LastReferencedBranch = null;
        LastFilterPattern = null;
        LastSearchTerm = null;
    }

    public void UpdateFrom(TranslationResult translation, ExecutionResult? execution)
    {
        if (translation.ExtractedEntities == null) return;

        var entities = translation.ExtractedEntities;
        if (entities.Paths.Count > 0)
        {
            LastReferencedPaths = new List<string>(entities.Paths);
        }
        if (entities.ProcessIds.Count > 0)
        {
            LastReferencedProcessIds = new List<int>(entities.ProcessIds);
        }
        if (entities.ProcessNames.Count > 0)
        {
            LastReferencedProcessNames = new List<string>(entities.ProcessNames);
        }
        if (entities.Ports.Count > 0)
        {
            LastReferencedPorts = new List<int>(entities.Ports);
        }
        if (entities.Services.Count > 0)
        {
            LastReferencedServices = new List<string>(entities.Services);
        }
        if (!string.IsNullOrWhiteSpace(entities.TargetUrlOrHost))
        {
            LastReferencedUrlOrHost = entities.TargetUrlOrHost;
        }
        if (!string.IsNullOrWhiteSpace(entities.FilterPattern))
        {
            LastFilterPattern = entities.FilterPattern;
        }
        if (!string.IsNullOrWhiteSpace(entities.LastReferencedBranch))
        {
            LastReferencedBranch = entities.LastReferencedBranch;
        }
        if (!string.IsNullOrWhiteSpace(entities.SearchTerm))
        {
            LastSearchTerm = entities.SearchTerm;
        }
    }
}

public class ExtractedEntityBag
{
    public List<string> Paths { get; set; } = new();
    public List<int> ProcessIds { get; set; } = new();
    public List<string> ProcessNames { get; set; } = new();
    public List<int> Ports { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public string? TargetUrlOrHost { get; set; }
    public string? LastReferencedBranch { get; set; }
    public string? FilterPattern { get; set; }
    public string? SearchTerm { get; set; }
}
