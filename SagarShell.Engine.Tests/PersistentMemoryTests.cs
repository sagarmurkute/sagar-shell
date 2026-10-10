using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Memory;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class PersistentMemoryTests : IDisposable
{
    private readonly string _testStorageDir;

    public PersistentMemoryTests()
    {
        _testStorageDir = Path.Combine(Path.GetTempPath(), "SagarShell_Tests_Memory_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testStorageDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testStorageDir))
            {
                Directory.Delete(_testStorageDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SessionPersistence_SavesAndReloadsAcrossRestarts()
    {
        var store = new LocalSessionStore(_testStorageDir);

        var session = new SessionSnapshot
        {
            SessionId = "session_123",
            InitialDirectory = @"C:\Users\User\Projects",
            FinalDirectory = @"C:\Users\User\Projects\App",
            IsElevated = false
        };

        session.Commands.Add(new HistoricalCommandRecord
        {
            SessionId = session.SessionId,
            RawInput = "list files in downloads",
            SanitizedInput = "list files in downloads",
            PowerShellScript = "Get-ChildItem -Path \"$HOME\\Downloads\"",
            Explanation = "Lists files in Downloads.",
            Category = IntentCategory.FileSystem,
            Success = true,
            WorkingDirectory = @"C:\Users\User\Projects"
        });

        session.Commands.Add(new HistoricalCommandRecord
        {
            SessionId = session.SessionId,
            RawInput = "show running processes",
            SanitizedInput = "show running processes",
            PowerShellScript = "Get-Process | Select-Object Name, Id, WorkingSet64",
            Explanation = "Displays active processes.",
            Category = IntentCategory.ProcessManagement,
            Success = true,
            WorkingDirectory = @"C:\Users\User\Projects\App"
        });

        await store.SaveSessionAsync(session);

        // Simulate new shell instance reload
        var reloadedStore = new LocalSessionStore(_testStorageDir);
        var loadedSession = await reloadedStore.LoadSessionAsync("session_123");

        Assert.NotNull(loadedSession);
        Assert.Equal(2, loadedSession.TotalCommands);
        Assert.Equal(@"C:\Users\User\Projects\App", loadedSession.FinalDirectory);
        Assert.Equal("list files in downloads", loadedSession.Commands[0].RawInput);
        Assert.Equal("show running processes", loadedSession.Commands[1].RawInput);
    }

    [Fact]
    public void NaturalHistorySearch_YesterdayQuery_FindsMatchingTemporalCommand()
    {
        var history = new System.Collections.Generic.List<HistoricalCommandRecord>
        {
            new()
            {
                Timestamp = DateTime.UtcNow.Date.AddDays(-1).AddHours(14), // Yesterday at 2 PM
                RawInput = "find large files in downloads",
                SanitizedInput = "find large files in downloads",
                PowerShellScript = "Get-ChildItem -Path \"$HOME\\Downloads\" | Where-Object Length -gt 100MB",
                Category = IntentCategory.FileSystem,
                Success = true
            },
            new()
            {
                Timestamp = DateTime.UtcNow, // Today
                RawInput = "ping google.com",
                SanitizedInput = "ping google.com",
                PowerShellScript = "Test-Connection google.com",
                Category = IntentCategory.Network,
                Success = true
            }
        };

        var search = NaturalHistorySearchEngine.Search("show the command I used yesterday to find large files", history);

        Assert.NotNull(search);
        Assert.Single(search.Matches);
        Assert.Contains("large files", search.Matches[0].RawInput);
        Assert.Contains("yesterday", search.QueryExplanation);
    }

    [Fact]
    public void NaturalHistorySearch_IntentAndKeywords_FindsPortCommand()
    {
        var history = new System.Collections.Generic.List<HistoricalCommandRecord>
        {
            new()
            {
                RawInput = "kill whatever is using port 3000",
                SanitizedInput = "kill whatever is using port 3000",
                PowerShellScript = "Get-NetTCPConnection -LocalPort 3000 | Stop-Process",
                Category = IntentCategory.Network,
                Success = true
            },
            new()
            {
                RawInput = "git commit with message 'fix bug'",
                SanitizedInput = "git commit with message 'fix bug'",
                PowerShellScript = "git commit -m 'fix bug'",
                Category = IntentCategory.GitOperations,
                Success = true
            }
        };

        var search = NaturalHistorySearchEngine.Search("what was the command I used to kill port 3000?", history);

        Assert.Single(search.Matches);
        Assert.Equal("kill whatever is using port 3000", search.Matches[0].RawInput);
    }

    [Fact]
    public void PrivacySanitizer_RedactsApiKeys_Passwords_AndBearerTokens()
    {
        var rawInput = "set env OPENAI_KEY to 'sk-abcdef12345678901234567890' with --password 'SuperSecretP@ssword123'";
        var sanitized = PrivacySanitizer.Sanitize(rawInput);

        Assert.DoesNotContain("sk-abcdef12345678901234567890", sanitized);
        Assert.DoesNotContain("SuperSecretP@ssword123", sanitized);
        Assert.Contains("[REDACTED_OPENAI_KEY]", sanitized);
        Assert.Contains("[REDACTED_PASSWORD]", sanitized);
    }

    [Fact]
    public async Task ContextSegregation_ArchivedSessionsDoNotPolluteActivePronounResolution()
    {
        var store = new LocalSessionStore(_testStorageDir);
        var oldSession = new SessionSnapshot
        {
            SessionId = "old_archived_session"
        };
        oldSession.Commands.Add(new HistoricalCommandRecord
        {
            SessionId = oldSession.SessionId,
            RawInput = "list files in C:\\OldStaleFolder",
            SanitizedInput = "list files in C:\\OldStaleFolder",
            PowerShellScript = "Get-ChildItem -Path 'C:\\OldStaleFolder'",
            Category = IntentCategory.FileSystem,
            ExtractedPaths = new System.Collections.Generic.List<string> { "C:\\OldStaleFolder" }
        });
        await store.SaveSessionAsync(oldSession);

        // Start fresh active session
        var freshContext = new ShellContext(@"C:\Users\User\FreshSession");
        var engine = new NaturalLanguageEngine();

        // In fresh context without history, "delete them" should not guess C:\OldStaleFolder
        var req = new TranslationRequest("delete them", freshContext);
        var translation = engine.Translate(req);

        // Must not delete the old session path
        Assert.DoesNotContain("OldStaleFolder", translation.PowerShellScript);
    }

    [Fact]
    public async Task PurgeHistory_DeletesAllSavedSessionsCleanly()
    {
        var store = new LocalSessionStore(_testStorageDir);
        var session = new SessionSnapshot { SessionId = "purge_test_session" };
        session.Commands.Add(new HistoricalCommandRecord
        {
            SessionId = session.SessionId,
            RawInput = "git status",
            SanitizedInput = "git status",
            PowerShellScript = "git status",
            Category = IntentCategory.GitOperations
        });
        await store.SaveSessionAsync(session);

        var sessionsBefore = await store.GetAllSessionsAsync();
        Assert.NotEmpty(sessionsBefore);

        store.PurgeAllHistory();

        var sessionsAfter = await store.GetAllSessionsAsync();
        Assert.Empty(sessionsAfter);
    }
}
