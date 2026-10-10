using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles process management: listing, finding by name/port/resource hogs, killing by name/PID/port, and launching apps.
/// </summary>
public class ProcessPatterns : ICommandPattern
{
    public int Priority => 85;

    // List processes
    private static readonly Regex ListProcessesRegex = new(
        @"^(?:show|list|get|display)\s+(?:all\s+)?(?:running\s+)?processes?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Top CPU / RAM processes: "top 5 memory hogs", "what is using the most cpu", "top 10 processes by ram", "top cpu processes"
    private static readonly Regex TopResourceRegex = new(
        @"(?:top\s+(?<count>\d+)?\s*(?:processes?\s+by\s+|by\s+)?(?<resource>cpu|memory|ram|mem)(?:\s+(?:hogs|consumers|users|processes))?|(?:what|which\s+process)\s+is\s+using\s+(?:the\s+)?most\s+(?<resource>cpu|memory|ram|mem))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Find process by name: "find process chrome", "is docker running", "check if slack is open"
    private static readonly Regex FindProcessRegex = new(
        @"^(?:find|search|check|is|look\s+for)\s+(?:process\s+)?(?<name>[a-zA-Z0-9_\-\.]+)(?:\s+(?:running|open|active))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Kill by name: "kill process chrome", "stop all node processes", "kill task spotify"
    private static readonly Regex KillByNameRegex = new(
        @"^(?:kill|terminate|stop|end)\s+(?:all\s+)?(?:process(?:es)?|task(?:s)?|app(?:s)?)\s+['""]?(?<name>[a-zA-Z0-9_\-\.]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Kill by PID: "kill pid 1234", "kill process with id 4567", "terminate process 890"
    private static readonly Regex KillByPidRegex = new(
        @"^(?:kill|terminate|stop|end)\s+(?:process\s+)?(?:with\s+)?(?:pid|id)\s+(?<pid>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Kill by Port: "kill process on port 3000", "kill whatever is using port 8080", "free port 5000"
    private static readonly Regex KillByPortRegex = new(
        @"(?:kill|terminate|stop|free)\s+(?:whatever\s+is\s+|process\s+)?(?:on|using|listening\s+on)\s+port\s+(?<port>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Find process on port: "what is using port 3000", "find process on port 8080"
    private static readonly Regex FindByPortRegex = new(
        @"(?:what|which\s+process|who|find\s+process)\s+(?:is\s+)?(?:using|listening\s+on|on)\s+port\s+(?<port>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Launch app: "start notepad", "launch chrome", "open calc", "open code"
    private static readonly Regex LaunchAppRegex = new(
        @"^(?:start|launch|run|open)\s+(?<app>notepad|calc|calculator|chrome|edge|firefox|code|explorer|cmd|powershell|taskmgr|mspaint|control|spotify|slack|discord)(?:\s+(?<args>.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "ps" or "top" or "tasklist"
            || ListProcessesRegex.IsMatch(input)
            || TopResourceRegex.IsMatch(input)
            || KillByNameRegex.IsMatch(input)
            || KillByPidRegex.IsMatch(input)
            || KillByPortRegex.IsMatch(input)
            || FindByPortRegex.IsMatch(input)
            || LaunchAppRegex.IsMatch(input)
            || (FindProcessRegex.IsMatch(input) && !lower.StartsWith("list files") && !lower.StartsWith("find files"));
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. Kill by Port
        var killPortMatch = KillByPortRegex.Match(input);
        if (killPortMatch.Success)
        {
            var port = int.Parse(killPortMatch.Groups["port"].Value);
            var ps = $"$procId = (Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue).OwningProcess; if ($procId) {{ Stop-Process -Id $procId -Force; Write-Host \"Killed process $procId on port {port}\" }} else {{ Write-Host \"No process found on port {port}\" }}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Finds and terminates the process occupying TCP port {port}.",
                RiskLevel.High,
                IntentCategory.ProcessManagement
            );
            result.ExtractedEntities!.Ports.Add(port);
            return result;
        }

        // 2. Find by Port
        var findPortMatch = FindByPortRegex.Match(input);
        if (findPortMatch.Success)
        {
            var port = int.Parse(findPortMatch.Groups["port"].Value);
            var ps = $"Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, State, OwningProcess, @{{Name='ProcessName'; Expression={{(Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName}}}}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Identifies the process listening on port {port}.",
                RiskLevel.Safe,
                IntentCategory.ProcessManagement
            );
            result.ExtractedEntities!.Ports.Add(port);
            result.SuggestedFollowUps.Add($"kill process on port {port}");
            return result;
        }

        // 3. Kill by PID
        var killPidMatch = KillByPidRegex.Match(input);
        if (killPidMatch.Success)
        {
            var pid = int.Parse(killPidMatch.Groups["pid"].Value);
            var ps = $"Stop-Process -Id {pid} -Force; Write-Host \"Terminated process with PID {pid}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Terminates process ID {pid}.",
                RiskLevel.High,
                IntentCategory.ProcessManagement
            );
            result.ExtractedEntities!.ProcessIds.Add(pid);
            return result;
        }

        // 4. Kill by Name
        var killNameMatch = KillByNameRegex.Match(input);
        if (killNameMatch.Success)
        {
            var name = killNameMatch.Groups["name"].Value.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            var ps = $"Stop-Process -Name '{name}' -Force -ErrorAction SilentlyContinue; Write-Host \"Stopped process(es) named '{name}'\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Terminates all running processes named '{name}'.",
                RiskLevel.High,
                IntentCategory.ProcessManagement
            );
            result.ExtractedEntities!.ProcessNames.Add(name);
            return result;
        }

        // 5. Launch App
        var launchMatch = LaunchAppRegex.Match(input);
        if (launchMatch.Success)
        {
            var app = launchMatch.Groups["app"].Value.Trim();
            var args = launchMatch.Groups["args"].Value.Trim();

            string ps = string.IsNullOrEmpty(args)
                ? $"Start-Process '{app}'"
                : $"Start-Process '{app}' -ArgumentList '{args.Replace("'", "''")}'";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Launches application '{app}'" + (string.IsNullOrEmpty(args) ? "." : $" with arguments '{args}'."),
                RiskLevel.Moderate,
                IntentCategory.WindowAndApp
            );
            result.ExtractedEntities!.ProcessNames.Add(app);
            return result;
        }

        // 6. Top CPU / RAM consumers
        var topResMatch = TopResourceRegex.Match(input);
        if (topResMatch.Success || lower.Contains("most cpu") || lower.Contains("most memory") || lower.Contains("most ram"))
        {
            var resType = topResMatch.Groups["resource"].Value.ToLowerInvariant();
            var isMem = resType is "memory" or "ram" or "mem" || lower.Contains("memory") || lower.Contains("ram");
            var countStr = topResMatch.Groups["count"].Value;
            var count = int.TryParse(countStr, out var c) && c > 0 ? c : 10;

            string ps = isMem
                ? $"Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First {count} ProcessName, Id, @{{Name='MemoryMB'; Expression={{[math]::Round($_.WorkingSet64 / 1MB, 2)}}}}, Description"
                : $"Get-Process | Sort-Object CPU -Descending | Select-Object -First {count} ProcessName, Id, @{{Name='CPU(s)'; Expression={{[math]::Round($_.CPU, 2)}}}}, @{{Name='MemoryMB'; Expression={{[math]::Round($_.WorkingSet64 / 1MB, 2)}}}}, Description";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Displays top {count} processes by {(isMem ? "RAM/Memory" : "CPU")} usage.",
                RiskLevel.Safe,
                IntentCategory.ProcessManagement
            );
            result.SuggestedFollowUps.Add("kill it");
            result.SuggestedFollowUps.Add("sort by size");
            return result;
        }

        // 7. Find process by name
        var findMatch = FindProcessRegex.Match(input);
        if (findMatch.Success)
        {
            var name = findMatch.Groups["name"].Value.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            var ps = $"Get-Process -Name '{name}*' -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, @{{Name='MemoryMB'; Expression={{[math]::Round($_.WorkingSet64 / 1MB, 2)}}}}, CPU, StartTime";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Checks for running process(es) matching '{name}'.",
                RiskLevel.Safe,
                IntentCategory.ProcessManagement
            );
            result.ExtractedEntities!.ProcessNames.Add(name);
            result.SuggestedFollowUps.Add($"kill process {name}");
            return result;
        }

        // 8. General process list
        return TranslationResult.CreateSuccess(
            input,
            "Get-Process | Select-Object -First 30 Id, ProcessName, @{{Name='MemoryMB'; Expression={{[math]::Round($_.WorkingSet64 / 1MB, 2)}}}}, CPU | Sort-Object ProcessName",
            "Lists all active running processes.",
            RiskLevel.Safe,
            IntentCategory.ProcessManagement
        );
    }
}
