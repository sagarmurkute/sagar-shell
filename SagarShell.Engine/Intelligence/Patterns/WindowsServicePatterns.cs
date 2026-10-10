using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles Windows Service control: listing, querying status, starting, stopping, and restarting services.
/// </summary>
public class WindowsServicePatterns : ICommandPattern
{
    public int Priority => 82;

    // List services: "list services", "show running services", "get services"
    private static readonly Regex ListServicesRegex = new(
        @"\b(?:show|list|get|display)\s+(?:all\s+)?(?:running\s+)?(?:windows\s+)?services\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Status / Check: "check service wuauserv", "status of service spooler", "is spooler running"
    private static readonly Regex CheckServiceRegex = new(
        @"(?:status\s+of\s+(?:service\s+)?|check\s+(?:service\s+)?|is\s+(?:service\s+)?)['""]?(?<name>[a-zA-Z0-9_\-\.]+)(?:['""]?)?\s*(?:running|active|service)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Start service: "start service spooler", "start the wuauserv service"
    private static readonly Regex StartServiceRegex = new(
        @"^start\s+(?:the\s+)?(?:service\s+)?['""]?(?<name>[a-zA-Z0-9_\-\.]+)['""]?(?:\s+service)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Stop service: "stop service spooler", "stop the wuauserv service"
    private static readonly Regex StopServiceRegex = new(
        @"^stop\s+(?:the\s+)?(?:service\s+)?['""]?(?<name>[a-zA-Z0-9_\-\.]+)['""]?(?:\s+service)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Restart service: "restart service spooler", "restart docker service"
    private static readonly Regex RestartServiceRegex = new(
        @"^restart\s+(?:the\s+)?(?:service\s+)?['""]?(?<name>[a-zA-Z0-9_\-\.]+)['""]?(?:\s+service)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return (lower.Contains("service") || lower.Contains("services"))
            && (ListServicesRegex.IsMatch(input)
                || CheckServiceRegex.IsMatch(input)
                || StartServiceRegex.IsMatch(input)
                || StopServiceRegex.IsMatch(input)
                || RestartServiceRegex.IsMatch(input));
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        // 1. Restart Service
        var restartMatch = RestartServiceRegex.Match(input);
        if (restartMatch.Success)
        {
            var name = restartMatch.Groups["name"].Value.Trim();
            var ps = $"Restart-Service -Name '{name}' -Force; Write-Host \"Restarted service: '{name}'\"";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Restarts Windows service '{name}'. Requires Administrator privileges.",
                RiskLevel.High,
                IntentCategory.WindowsServices
            );
            result.RequiresElevation = true;
            result.ExtractedEntities!.Services.Add(name);
            return result;
        }

        // 2. Stop Service
        var stopMatch = StopServiceRegex.Match(input);
        if (stopMatch.Success)
        {
            var name = stopMatch.Groups["name"].Value.Trim();
            var ps = $"Stop-Service -Name '{name}' -Force; Write-Host \"Stopped service: '{name}'\"";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Stops Windows service '{name}'. Requires Administrator privileges.",
                RiskLevel.High,
                IntentCategory.WindowsServices
            );
            result.RequiresElevation = true;
            result.ExtractedEntities!.Services.Add(name);
            return result;
        }

        // 3. Start Service
        var startMatch = StartServiceRegex.Match(input);
        if (startMatch.Success)
        {
            var name = startMatch.Groups["name"].Value.Trim();
            var ps = $"Start-Service -Name '{name}'; Write-Host \"Started service: '{name}'\"";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Starts Windows service '{name}'. Requires Administrator privileges.",
                RiskLevel.Moderate,
                IntentCategory.WindowsServices
            );
            result.RequiresElevation = true;
            result.ExtractedEntities!.Services.Add(name);
            return result;
        }

        // 4. Check Service Status
        var checkMatch = CheckServiceRegex.Match(input);
        if (checkMatch.Success)
        {
            var name = checkMatch.Groups["name"].Value.Trim();
            var ps = $"Get-Service -Name '{name}*' | Select-Object Name, DisplayName, Status, StartType";
            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Queries service status for '{name}'.",
                RiskLevel.Safe,
                IntentCategory.WindowsServices
            );
            result.ExtractedEntities!.Services.Add(name);
            result.SuggestedFollowUps.Add($"restart service {name}");
            result.SuggestedFollowUps.Add($"stop service {name}");
            return result;
        }

        // 5. List Services
        return TranslationResult.CreateSuccess(
            input,
            "Get-Service | Where-Object { $_.Status -eq 'Running' } | Select-Object -First 30 Name, DisplayName, Status, StartType | Sort-Object Name",
            "Lists active running Windows services.",
            RiskLevel.Safe,
            IntentCategory.WindowsServices
        );
    }
}
