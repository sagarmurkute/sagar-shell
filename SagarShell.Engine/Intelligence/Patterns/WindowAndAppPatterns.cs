using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles desktop/windowing actions: wallpaper, clipboard, browser URLs, volume, and terminal UI state.
/// </summary>
public class WindowAndAppPatterns : ICommandPattern
{
    public int Priority => 75;

    // Wallpaper: "set wallpaper to 'C:\bg.jpg'", "change wallpaper to image.png"
    private static readonly Regex WallpaperRegex = new(
        @"(?:set|change)\s+wallpaper(?:\s+to)?\s+['""]?(?<path>[^'""]+)['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Clipboard: "show clipboard", "get clipboard", "copy 'test' to clipboard", "clear clipboard"
    private static readonly Regex GetClipboardRegex = new(
        @"\b(?:show|get|view|paste)\s+clipboard\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SetClipboardRegex = new(
        @"(?:copy|set\s+clipboard\s+to)\s+['""](?<text>[^'""]+)['""](?:\s+to\s+clipboard)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClearClipboardRegex = new(
        @"\b(?:clear|empty)\s+clipboard\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Browser / Search: "search google for 'best c# features'", "open https://github.com"
    private static readonly Regex GoogleSearchRegex = new(
        @"(?:search\s+(?:google\s+for|the\s+web\s+for|for)?|google)\s+['""]?(?<query>[^'""]+)['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpenUrlRegex = new(
        @"^(?:open|browse|navigate\s+to)\s+(?<url>https?:\/\/[^\s]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Clear Screen: "clear screen", "cls", "clear"
    private static readonly Regex ClearScreenRegex = new(
        @"^(?:clear\s+screen|clear|cls|clean\s+screen)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "cls" or "clear"
            || ClearScreenRegex.IsMatch(input)
            || WallpaperRegex.IsMatch(input)
            || GetClipboardRegex.IsMatch(input)
            || SetClipboardRegex.IsMatch(input)
            || ClearClipboardRegex.IsMatch(input)
            || OpenUrlRegex.IsMatch(input)
            || GoogleSearchRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        // 1. Clear Screen
        if (ClearScreenRegex.IsMatch(input) || input.ToLowerInvariant() is "cls" or "clear")
        {
            return TranslationResult.CreateSuccess(
                input,
                "Clear-Host",
                "Clears the terminal console buffer.",
                RiskLevel.Low,
                IntentCategory.WindowAndApp
            );
        }

        // 2. Wallpaper
        var wallMatch = WallpaperRegex.Match(input);
        if (wallMatch.Success)
        {
            var path = wallMatch.Groups["path"].Value.Trim();
            var resolved = NaturalPathResolver.ResolvePath(path);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);
            var ps = $"Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public class W {{ [DllImport(\"user32.dll\", SetLastError=true)] public static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni); }}'; [W]::SystemParametersInfo(20, 0, (Resolve-Path {escaped}).Path, 3); Write-Host \"Desktop wallpaper updated to {path}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Updates Windows desktop background wallpaper to '{path}'.",
                RiskLevel.Moderate,
                IntentCategory.WindowAndApp
            );
            result.ExtractedEntities!.Paths.Add(path);
            return result;
        }

        // 3. Clipboard Get / Set / Clear
        if (ClearClipboardRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Set-Clipboard -Value $null; Write-Host \"Clipboard cleared.\"",
                "Clears the Windows clipboard content.",
                RiskLevel.Low,
                IntentCategory.WindowAndApp
            );
        }

        var setClipMatch = SetClipboardRegex.Match(input);
        if (setClipMatch.Success)
        {
            var text = setClipMatch.Groups["text"].Value.Replace("'", "''");
            var ps = $"Set-Clipboard -Value '{text}'; Write-Host \"Copied to clipboard.\"";
            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Copies the specified text into the clipboard.",
                RiskLevel.Low,
                IntentCategory.WindowAndApp
            );
        }

        if (GetClipboardRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-Clipboard",
                "Reads and outputs text currently in the clipboard.",
                RiskLevel.Safe,
                IntentCategory.WindowAndApp
            );
        }

        // 4. Open URL
        var urlMatch = OpenUrlRegex.Match(input);
        if (urlMatch.Success)
        {
            var url = urlMatch.Groups["url"].Value.Trim();
            var ps = $"Start-Process '{url}'";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Opens URL '{url}' in the default web browser.",
                RiskLevel.Low,
                IntentCategory.WindowAndApp
            );
            result.ExtractedEntities!.TargetUrlOrHost = url;
            return result;
        }

        // 5. Web search
        var searchMatch = GoogleSearchRegex.Match(input);
        if (searchMatch.Success)
        {
            var query = Uri.EscapeDataString(searchMatch.Groups["query"].Value.Trim());
            var url = $"https://www.google.com/search?q={query}";
            var ps = $"Start-Process '{url}'";
            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Performs a web search for '{searchMatch.Groups["query"].Value.Trim()}'.",
                RiskLevel.Low,
                IntentCategory.WindowAndApp
            );
        }

        return TranslationResult.CreateUnknown(input);
    }
}
