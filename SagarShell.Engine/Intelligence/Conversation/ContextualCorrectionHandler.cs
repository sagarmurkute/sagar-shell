using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Conversation;

/// <summary>
/// Handles conversational user corrections where the user clarifies, corrects,
/// or substitutes a parameter/entity from the immediately preceding turn.
/// </summary>
public static class ContextualCorrectionHandler
{
    private static readonly Regex CorrectionIndicatorRegex = new(
        @"^(?:no|not\s+that|actually|wait|i\s+meant|instead)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortCorrectionRegex = new(
        @"\b(?:port\s+|on\s+port\s+|i\s+meant\s+port\s+)(?<port>\d{2,5})\b|\b(?:not\s+\d+,\s*(?:check\s+|use\s+)?)(?<port2>\d{2,5})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NameCorrectionRegex = new(
        @"\b(?:kill|terminate|stop|check|restart)\s+(?<name>[a-zA-Z0-9_\.-]+)(?:\s+instead)?\b|\b(?:actually\s+|no,\s+)(?<name2>[a-zA-Z0-9_\.-]+)(?:\s+not\s+[a-zA-Z0-9_\.-]+)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DrivePathCorrectionRegex = new(
        @"\b(?:on\s+drive\s+|in\s+folder\s+|in\s+directory\s+|in\s+)(?<path>[a-zA-Z]:(?:\\[^\s]*)?|[a-zA-Z0-9_\\.-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtensionCorrectionRegex = new(
        @"\b(?:only\s+)?(?:\*\.)?(?<ext>[a-zA-Z0-9_-]+)\s+files?(?:\s+instead)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsCorrection(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input) || context.History.Count == 0)
            return false;

        var clean = NaturalTextSanitizer.Sanitize(input).Trim();
        var raw = input.Trim();

        return CorrectionIndicatorRegex.IsMatch(raw) || CorrectionIndicatorRegex.IsMatch(clean);
    }

    public static TranslationResult? TryHandleCorrection(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input) || context.History.Count == 0)
            return null;

        var clean = NaturalTextSanitizer.Sanitize(input).Trim();
        var raw = input.Trim();
        var lastCmd = context.History[^1].Translation.PowerShellScript;
        var lastCategory = context.History[^1].Translation.Category;

        // 1. Port Correction: "no, I meant port 5000", "actually port 3000"
        var portMatch = PortCorrectionRegex.Match(raw);
        if (portMatch.Success)
        {
            var pStr = portMatch.Groups["port"].Success ? portMatch.Groups["port"].Value : portMatch.Groups["port2"].Value;
            if (int.TryParse(pStr, out int newPort))
            {
                var newScript = Regex.Replace(lastCmd, @"-LocalPort\s+\d+", $"-LocalPort {newPort}", RegexOptions.IgnoreCase);
                if (!newScript.Contains("-LocalPort"))
                {
                    newScript = $"Get-NetTCPConnection -LocalPort {newPort} -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, State, OwningProcess";
                }

                var res = TranslationResult.CreateSuccess(
                    input,
                    newScript,
                    $"Corrected target port to {newPort}.",
                    RiskLevel.Safe,
                    IntentCategory.Network
                );
                res.ConversationalMessage = $"Got it! Corrected to check port {newPort}.";
                res.IsConversationalResponse = true;
                return res;
            }
        }

        // 2. Drive / Path Correction: "actually on drive D:", "no, in folder C:\Logs"
        var pathMatch = DrivePathCorrectionRegex.Match(raw);
        if (pathMatch.Success)
        {
            var newPath = pathMatch.Groups["path"].Value;
            string newScript;
            if (lastCmd.Contains("-Path"))
            {
                newScript = Regex.Replace(lastCmd, @"-Path\s+['""]?[^'""\s]+['""]?", $"-Path '{newPath}'", RegexOptions.IgnoreCase);
            }
            else
            {
                newScript = $"Get-ChildItem -Path '{newPath}'";
            }

            var res = TranslationResult.CreateSuccess(
                input,
                newScript,
                $"Corrected search path to '{newPath}'.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
            res.ConversationalMessage = $"Updated path location to '{newPath}'.";
            res.IsConversationalResponse = true;
            return res;
        }

        // 3. File Extension Correction: "wait, only include .pdf files", "no, look for .log files instead"
        var extMatch = ExtensionCorrectionRegex.Match(raw);
        if (extMatch.Success)
        {
            var ext = extMatch.Groups["ext"].Value.TrimStart('.');
            if (!string.Equals(ext, "file", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, "files", StringComparison.OrdinalIgnoreCase))
            {
                string newScript;
                if (lastCmd.Contains("-Filter"))
                {
                    newScript = Regex.Replace(lastCmd, @"-Filter\s+['""]?[^'""\s]+['""]?", $"-Filter '*.{ext}'", RegexOptions.IgnoreCase);
                }
                else
                {
                    newScript = $"Get-ChildItem -Filter '*.{ext}'";
                }

                var res = TranslationResult.CreateSuccess(
                    input,
                    newScript,
                    $"Corrected file filter to '*.{ext}'.",
                    RiskLevel.Safe,
                    IntentCategory.FileSystem
                );
                res.ConversationalMessage = $"Updated filter to include only '*.{ext}' files.";
                res.IsConversationalResponse = true;
                return res;
            }
        }

        // 4. Target Name Correction: "not that one, kill chrome instead", "actually node, not python"
        var nameMatch = NameCorrectionRegex.Match(raw);
        if (nameMatch.Success)
        {
            var newName = nameMatch.Groups["name"].Success ? nameMatch.Groups["name"].Value : nameMatch.Groups["name2"].Value;
            if (!string.IsNullOrWhiteSpace(newName) && !newName.Equals("one", StringComparison.OrdinalIgnoreCase))
            {
                if (lastCmd.Contains("Stop-Process") || raw.Contains("kill") || raw.Contains("stop") || raw.Contains("terminate"))
                {
                    var script = $"Stop-Process -Name '{newName}' -Force";
                    var res = TranslationResult.CreateSuccess(
                        input,
                        script,
                        $"Corrected target process to '{newName}'.",
                        RiskLevel.High,
                        IntentCategory.ProcessManagement
                    );
                    res.RequiresExplicitConfirmation = true;
                    res.ImpactExplanation = $"Terminating process '{newName}' will close all running instances.";
                    res.ConversationalMessage = $"Updated target to '{newName}'. Please confirm before terminating.";
                    res.IsConversationalResponse = true;
                    return res;
                }
                else if (lastCmd.Contains("Restart-Service") || lastCmd.Contains("Get-Service"))
                {
                    var script = $"Get-Service -Name '{newName}'";
                    var res = TranslationResult.CreateSuccess(
                        input,
                        script,
                        $"Corrected service query to '{newName}'.",
                        RiskLevel.Safe,
                        IntentCategory.WindowsServices
                    );
                    res.ConversationalMessage = $"Updated target service to '{newName}'.";
                    res.IsConversationalResponse = true;
                    return res;
                }
            }
        }

        return null;
    }
}
