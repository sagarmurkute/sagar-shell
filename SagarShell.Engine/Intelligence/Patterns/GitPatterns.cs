using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles version control intents: status, commits, branches, history, pull/push, stash, and rollback.
/// </summary>
public class GitPatterns : ICommandPattern
{
    public int Priority => 84;

    // Status: "git status", "what changed", "show git status", "repo status"
    private static readonly Regex GitStatusRegex = new(
        @"\b(?:git\s+status|repo\s+status|what\s+changed(?:\s+in\s+git)?|git\s+changes|uncommitted\s+changes)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Commit: "commit everything with message 'feat: login'", "commit all with 'fix'", "save changes with message 'test'"
    private static readonly Regex GitCommitRegex = new(
        @"(?:commit\s+(?:all\s+|everything\s+)?(?:with\s+(?:message\s+)?)?|save\s+(?:all\s+)?changes\s+with\s+(?:message\s+)?)['""](?<msg>[^'""]+)['""]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Create / Switch Branch: "create branch feat/login", "switch to branch main", "checkout main"
    private static readonly Regex GitBranchRegex = new(
        @"(?:(?:create\s+(?:and\s+switch\s+to\s+)?(?:new\s+)?branch|switch\s+to\s+branch|checkout\s+(?:branch\s+)?))\s+['""]?(?<branch>[a-zA-Z0-9_\-\.\/]+)['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Git Log: "show last 5 commits", "show git history", "recent commits", "git log"
    private static readonly Regex GitLogRegex = new(
        @"\b(?:git\s+log|recent\s+commits|git\s+history|last\s+(?<count>\d+)?\s*commits?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Pull / Push
    private static readonly Regex GitPullRegex = new(
        @"\b(?:git\s+pull|pull\s+latest(?:\s+changes)?|update\s+from\s+remote|sync\s+repo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GitPushRegex = new(
        @"\b(?:git\s+push|push\s+(?:my\s+)?changes(?:\s+to\s+origin)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Stash
    private static readonly Regex GitStashRegex = new(
        @"\b(?:git\s+stash|stash\s+(?:my\s+)?changes|save\s+to\s+stash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GitStashPopRegex = new(
        @"\b(?:git\s+stash\s+pop|restore\s+stash|pop\s+stash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Undo / Discard
    private static readonly Regex GitUndoRegex = new(
        @"\b(?:undo\s+(?:last\s+)?commit|discard\s+(?:all\s+)?changes|revert\s+uncommitted\s+changes)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower.StartsWith("git ")
            || GitStatusRegex.IsMatch(input)
            || GitCommitRegex.IsMatch(input)
            || GitBranchRegex.IsMatch(input)
            || GitLogRegex.IsMatch(input)
            || GitPullRegex.IsMatch(input)
            || GitPushRegex.IsMatch(input)
            || GitStashRegex.IsMatch(input)
            || GitStashPopRegex.IsMatch(input)
            || GitUndoRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. Commit
        var commitMatch = GitCommitRegex.Match(input);
        if (commitMatch.Success)
        {
            var msg = commitMatch.Groups["msg"].Value.Replace("\"", "\\\"");
            var ps = $"git add -A; git commit -m \"{msg}\"";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Stages all changes and commits with message: '{msg}'.",
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
            result.SuggestedFollowUps.Add("push changes");
            result.SuggestedFollowUps.Add("show git log");
            return result;
        }

        // 2. Branch creation / switch
        var branchMatch = GitBranchRegex.Match(input);
        if (branchMatch.Success)
        {
            var branch = branchMatch.Groups["branch"].Value.Trim();
            bool isNew = lower.Contains("create") || lower.Contains("new");
            var ps = isNew
                ? $"git checkout -b '{branch}'"
                : $"git checkout '{branch}'";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                (isNew ? $"Creates and switches to branch '{branch}'." : $"Switches to branch '{branch}'."),
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
            result.ExtractedEntities!.LastReferencedBranch = branch;
            return result;
        }

        // 3. Log
        var logMatch = GitLogRegex.Match(input);
        if (logMatch.Success)
        {
            var countStr = logMatch.Groups["count"].Value;
            var count = int.TryParse(countStr, out var c) && c > 0 ? c : 10;
            var ps = $"git log -n {count} --oneline --graph --decorate";

            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Shows the last {count} Git commits in condensed format.",
                RiskLevel.Safe,
                IntentCategory.GitOperations
            );
        }

        // 4. Stash Pop
        if (GitStashPopRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "git stash pop",
                "Restores and pops the most recently stashed changes.",
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
        }

        // 5. Stash
        if (GitStashRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "git stash",
                "Stashes uncommitted working tree changes.",
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
        }

        // 6. Undo / Discard
        if (GitUndoRegex.IsMatch(input))
        {
            if (lower.Contains("commit"))
            {
                return TranslationResult.CreateSuccess(
                    input,
                    "git reset --soft HEAD~1",
                    "Undoes the most recent commit while preserving changes in staging.",
                    RiskLevel.High,
                    IntentCategory.GitOperations
                );
            }
            return TranslationResult.CreateSuccess(
                input,
                "git restore .",
                "Discards all uncommitted changes in tracked files.",
                RiskLevel.High,
                IntentCategory.GitOperations
            );
        }

        // 7. Pull
        if (GitPullRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "git pull",
                "Fetches and integrates remote changes into the current branch.",
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
        }

        // 8. Push
        if (GitPushRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "git push",
                "Pushes committed commits to the upstream remote branch.",
                RiskLevel.Moderate,
                IntentCategory.GitOperations
            );
        }

        // 9. Status
        return TranslationResult.CreateSuccess(
            input,
            "git status -s",
            "Displays the concise status of the Git working tree.",
            RiskLevel.Safe,
            IntentCategory.GitOperations
        );
    }
}
