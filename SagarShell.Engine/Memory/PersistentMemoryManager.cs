using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Memory;

/// <summary>
/// Master Persistent Memory Manager.
/// Manages session persistence across restarts, historical query search, and context boundary isolation.
/// </summary>
public class PersistentMemoryManager
{
    private readonly LocalSessionStore _store;
    private readonly SessionSnapshot _activeSession;

    public SessionSnapshot ActiveSession => _activeSession;
    public LocalSessionStore Store => _store;

    public PersistentMemoryManager(string? initialDirectory = null, bool isElevated = false, string? customStorageDirectory = null)
    {
        _store = new LocalSessionStore(customStorageDirectory);
        _activeSession = new SessionSnapshot
        {
            InitialDirectory = initialDirectory ?? Environment.CurrentDirectory,
            FinalDirectory = initialDirectory ?? Environment.CurrentDirectory,
            IsElevated = isElevated
        };
    }

    /// <summary>
    /// Records an executed command into the active session memory.
    /// </summary>
    public void RecordExecution(
        string rawInput,
        string sanitizedInput,
        TranslationResult translation,
        ExecutionResult result,
        ShellContext context)
    {
        var record = new HistoricalCommandRecord
        {
            SessionId = _activeSession.SessionId,
            Timestamp = DateTime.UtcNow,
            RawInput = rawInput,
            SanitizedInput = sanitizedInput,
            PowerShellScript = translation.PowerShellScript,
            Explanation = translation.Explanation,
            Category = translation.Category,
            Risk = translation.Risk,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Duration = result.Duration,
            OutputSummary = !string.IsNullOrEmpty(result.StandardOutput) 
                ? (result.StandardOutput.Length > 200 ? result.StandardOutput[..200] + "..." : result.StandardOutput) 
                : (!string.IsNullOrEmpty(result.StandardError) ? result.StandardError : null),
            WorkingDirectory = context.CurrentDirectory,
            ExtractedPaths = translation.ExtractedEntities?.Paths.ToList() ?? new List<string>()
        };

        _activeSession.Commands.Add(record);
        _activeSession.FinalDirectory = context.CurrentDirectory;
    }

    /// <summary>
    /// Searches persistent memory using natural language temporal & keyword queries.
    /// </summary>
    public async Task<NaturalHistorySearchEngine.HistorySearchResult> SearchHistoryAsync(string naturalQuery)
    {
        var pastCommands = await _store.GetAllCommandsAsync(1000);
        
        // Combine active session commands with archived commands (active session records take precedence if recent)
        var allCombined = _activeSession.Commands
            .Concat(pastCommands.Where(p => p.SessionId != _activeSession.SessionId))
            .OrderByDescending(c => c.Timestamp)
            .ToList();

        return NaturalHistorySearchEngine.Search(naturalQuery, allCombined);
    }

    /// <summary>
    /// Saves the active session snapshot to disk.
    /// </summary>
    public async Task SaveActiveSessionAsync()
    {
        _activeSession.EndTime = DateTime.UtcNow;
        if (_activeSession.Commands.Count > 0)
        {
            await _store.SaveSessionAsync(_activeSession);
        }
    }

    /// <summary>
    /// Purges all local persistent session files.
    /// </summary>
    public void PurgeAllHistory()
    {
        _activeSession.Commands.Clear();
        _store.PurgeAllHistory();
    }
}
