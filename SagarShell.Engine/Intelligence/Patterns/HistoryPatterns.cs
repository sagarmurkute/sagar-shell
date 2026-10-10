using System;
using System.Text;
using System.Text.RegularExpressions;
using SagarShell.Engine.Memory;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles natural language history search ("show the command I used yesterday to find large files", "clear history").
/// </summary>
public class HistoryPatterns : ICommandPattern
{
    public int Priority => 92;

    private static readonly Regex ClearHistoryRegex = new(
        @"\b(?:clear\s+history|purge\s+history|delete\s+history|clean\s+history|clear\s+memory|purge\s+memory)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return ClearHistoryRegex.IsMatch(lower) || NaturalHistorySearchEngine.IsHistorySearchQuery(lower);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. Purge / Clear History
        if (ClearHistoryRegex.IsMatch(lower))
        {
            var res = TranslationResult.CreateSuccess(
                input,
                "Clear-History",
                "Purges persistent and in-memory command history.",
                RiskLevel.Moderate,
                IntentCategory.SystemInformation
            );

            res.DirectNativeAction = ctx =>
            {
                var store = new LocalSessionStore();
                store.PurgeAllHistory();
                ctx.History.Clear();
                ctx.Memory.Clear();
                return ExecutionResult.CreateSuccess("Persistent and active command history purged successfully.", TimeSpan.Zero, ctx.CurrentDirectory);
            };

            return res;
        }

        // 2. Natural Language History Search
        var searchResult = TranslationResult.CreateSuccess(
            input,
            "Get-History | Select-Object -Last 20",
            "Searches persistent command history matching your query.",
            RiskLevel.Safe,
            IntentCategory.SystemInformation
        );

        searchResult.DirectNativeAction = ctx =>
        {
            var store = new LocalSessionStore();
            var pastCommands = store.GetAllCommandsAsync(1000).GetAwaiter().GetResult();

            var search = NaturalHistorySearchEngine.Search(input, pastCommands);
            var sb = new StringBuilder();

            sb.AppendLine($"📜 {search.QueryExplanation}");
            if (search.Matches.Count > 0)
            {
                sb.AppendLine(new string('-', 70));
                for (int i = 0; i < search.Matches.Count; i++)
                {
                    var m = search.Matches[i];
                    var status = m.Success ? "[✓]" : "[✗]";
                    sb.AppendLine($"[{i + 1}] {m.Timestamp:yyyy-MM-dd HH:mm} {status} \"{m.RawInput}\"");
                    sb.AppendLine($"    Script: {m.PowerShellScript}");
                }
            }

            return ExecutionResult.CreateSuccess(sb.ToString().TrimEnd(), TimeSpan.Zero, ctx.CurrentDirectory);
        };

        return searchResult;
    }
}
