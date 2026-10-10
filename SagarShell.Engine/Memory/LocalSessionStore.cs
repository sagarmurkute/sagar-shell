using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SagarShell.Engine.Memory;

/// <summary>
/// Persists interactive sessions and commands to local disk in JSON format.
/// </summary>
public class LocalSessionStore
{
    private readonly string _storageDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string StorageDirectory => _storageDirectory;

    public LocalSessionStore(string? customStorageDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customStorageDirectory))
        {
            _storageDirectory = customStorageDirectory;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _storageDirectory = Path.Combine(localAppData, "SagarShell", "memory");
        }

        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task SaveSessionAsync(SessionSnapshot session)
    {
        if (session == null || session.Commands.Count == 0)
            return;

        var filePath = GetSessionFilePath(session.SessionId);

        // Sanitize every command before saving
        foreach (var cmd in session.Commands)
        {
            cmd.RawInput = PrivacySanitizer.Sanitize(cmd.RawInput);
            cmd.SanitizedInput = PrivacySanitizer.Sanitize(cmd.SanitizedInput);
            cmd.PowerShellScript = PrivacySanitizer.Sanitize(cmd.PowerShellScript);
            cmd.Explanation = PrivacySanitizer.Sanitize(cmd.Explanation);
            if (!string.IsNullOrEmpty(cmd.OutputSummary))
            {
                cmd.OutputSummary = PrivacySanitizer.Sanitize(cmd.OutputSummary);
            }
        }

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        // Prune old sessions if count exceeds limit (default: 100 sessions)
        PruneOldSessions(maxSessions: 100);
    }

    public async Task<SessionSnapshot?> LoadSessionAsync(string sessionId)
    {
        var filePath = GetSessionFilePath(sessionId);
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<SessionSnapshot>(json, JsonOptions);
    }

    public async Task<List<SessionSnapshot>> GetAllSessionsAsync(int maxSessions = 50)
    {
        var files = Directory.GetFiles(_storageDirectory, "session_*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(maxSessions);

        var sessions = new List<SessionSnapshot>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file.FullName);
                var session = JsonSerializer.Deserialize<SessionSnapshot>(json, JsonOptions);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }
            catch
            {
                // Ignore corrupted session files
            }
        }

        return sessions;
    }

    public async Task<List<HistoricalCommandRecord>> GetAllCommandsAsync(int maxCommands = 1000)
    {
        var sessions = await GetAllSessionsAsync(100);
        return sessions
            .SelectMany(s => s.Commands)
            .OrderByDescending(c => c.Timestamp)
            .Take(maxCommands)
            .ToList();
    }

    public void PurgeAllHistory()
    {
        if (Directory.Exists(_storageDirectory))
        {
            var files = Directory.GetFiles(_storageDirectory, "session_*.json");
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    private string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(_storageDirectory, $"session_{sessionId}.json");
    }

    private void PruneOldSessions(int maxSessions)
    {
        try
        {
            var files = Directory.GetFiles(_storageDirectory, "session_*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(maxSessions)
                .ToList();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch
        {
            // Ignore background pruning errors
        }
    }
}
