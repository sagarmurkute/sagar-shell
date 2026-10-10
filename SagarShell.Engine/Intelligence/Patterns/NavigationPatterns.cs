using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles directory navigation: cd, go to desktop/downloads/home, go back, up one folder.
/// </summary>
public class NavigationPatterns : ICommandPattern
{
    public int Priority => 95; // High priority for simple navigation

    private static readonly Regex GoBackRegex = new(
        @"^(?:go\s+back|back|cd\s+-|previous\s+folder|previous\s+directory)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CdRegex = new(
        @"^(?:cd|go\s+to|change\s+directory\s+to|navigate\s+to|jump\s+to|open\s+folder)(?:\s+['""]?(?<path>[^'""]+)['""]?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "cd .." or "cd" or "cd ~" or "cd -" or "back" or ".."
            || GoBackRegex.IsMatch(input)
            || CdRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        // 1. Go back
        if (GoBackRegex.IsMatch(input) || input.Trim() == "cd -")
        {
            var prev = context?.PreviousDirectory;
            if (!string.IsNullOrEmpty(prev))
            {
                var esc = NaturalPathResolver.EscapeForPowerShell(prev);
                var result = TranslationResult.CreateSuccess(
                    input,
                    $"Set-Location -Path {esc}",
                    $"Navigates back to previous directory: {prev}",
                    RiskLevel.Low,
                    IntentCategory.Navigation
                );
                result.ExtractedEntities!.Paths.Add(prev);
                return result;
            }
            else
            {
                return TranslationResult.CreateSuccess(
                    input,
                    "Write-Host \"No previous directory stored in history.\"",
                    "No previous directory in session history.",
                    RiskLevel.Safe,
                    IntentCategory.Navigation
                );
            }
        }

        // 2. Target path cd
        var trimmed = input.Trim();
        string rawPath = "~";

        if (trimmed is "cd .." or "..")
        {
            rawPath = "..";
        }
        else
        {
            var cdMatch = CdRegex.Match(trimmed);
            if (cdMatch.Success && cdMatch.Groups["path"].Success)
            {
                rawPath = cdMatch.Groups["path"].Value.Trim();
            }
        }

        var resolved = NaturalPathResolver.ResolvePath(rawPath);
        var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);

        var res = TranslationResult.CreateSuccess(
            input,
            $"Set-Location -Path {escaped}",
            $"Changes current working directory to '{resolved}'.",
            RiskLevel.Low,
            IntentCategory.Navigation
        );
        res.ExtractedEntities!.Paths.Add(resolved);
        res.SuggestedFollowUps.Add("list files");
        res.SuggestedFollowUps.Add("show git status");

        return res;
    }
}
