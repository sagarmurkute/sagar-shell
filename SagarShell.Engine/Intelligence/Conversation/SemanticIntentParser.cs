using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Conversation;

/// <summary>
/// A semantic intent parser that understands varied natural English without hardcoded exact strings.
/// Extracts semantic actions, entity concepts, filters, ordering, and constraints from English input.
/// </summary>
public static class SemanticIntentParser
{
    public enum SemanticAction
    {
        Unknown,
        FindOrList,
        ReadOrInspect,
        TerminateOrKill,
        StartOrLaunch,
        RestartOrBounce,
        RemoveOrDelete,
        CreateOrMake,
        CopyOrMove,
        FilterOrSort,
        Export,
        DiagnoseOrCheck,
        Elevate
    }

    public enum EntityType
    {
        Unknown,
        FilesOrFolders,
        Processes,
        PortsOrConnections,
        Services,
        EnvironmentVariables,
        Git,
        DisksOrVolumes,
        SystemInformation
    }

    public class SemanticIntent
    {
        public SemanticAction Action { get; set; } = SemanticAction.Unknown;
        public EntityType TargetType { get; set; } = EntityType.Unknown;
        public string? TargetName { get; set; }
        public string? TargetPath { get; set; }
        public int? TargetPort { get; set; }
        public int? TargetPid { get; set; }
        public int? LimitCount { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; }
        public bool Recursive { get; set; }
        public string? FileExtension { get; set; }
        public string? SizeFilter { get; set; }
        public double Confidence { get; set; } = 0.9;
    }

    private static readonly HashSet<string> ExcludedExtensionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "files", "folder", "folders", "directory", "directories", "all", "the", "my", "any", "some",
        "list", "show", "find", "get", "read", "write", "delete", "remove", "open", "in", "from", "on", "to",
        "temp", "large", "old", "new", "recent", "created", "modified", "subfolders", "downloads", "documents", "desktop"
    };

    private static readonly HashSet<string> ExcludedFolderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "files", "folder", "folders", "directory", "directories", "all", "the", "my", "any", "some",
        "subfolders", "here", "current", "this", "system", "pc", "laptop"
    };

    private static readonly Regex ActionFindRegex = new(
        @"\b(?:find|search|locate|look\s+for|grab|fetch|get|list|show|display|view|see|hunt|hunt\s+down|check|inspect)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActionKillRegex = new(
        @"\b(?:kill|terminate|stop|end|halt|nuke|close|shut\s+down)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActionRestartRegex = new(
        @"\b(?:restart|bounce|reboot|reload)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ActionDeleteRegex = new(
        @"\b(?:delete|remove|erase|wipe|purge|clear|clean|trash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TopLimitRegex = new(
        @"\b(?:top|first|limit\s+to|only|the)\s+(?<num>\d+)\b|\b(?<num2>\d+)\s+(?:heaviest|largest|biggest|top|most|first)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortRegex = new(
        @"\b(?:port\s+|listening\s+on\s+(?:port\s+)?|on\s+port\s+|occupying\s+port\s+|occupying\s+|using\s+port\s+)(?<port>\d{2,5})\b|\b(?:what\s+is\s+using\s+port\s+)(?<port2>\d{2,5})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemoteHostRegex = new(
        @"\b(?:on|to|against)\s+(?<host>[a-zA-Z0-9_-]+\.[a-zA-Z]{2,}|[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FolderRegex = new(
        @"\b(?:in|inside|from|under)\s+(?:folder\s+|directory\s+)?(?<folder>downloads|documents|desktop|temp|[a-zA-Z]:(?:\\[^\s]*)?|[a-zA-Z0-9_\.-]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PidRegex = new(
        @"\b(?:pid|process\s+id)\s+(?<pid>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtensionRegex = new(
        @"\b\*\.(?<ext>[a-zA-Z0-9_-]+)\b|\b(?<ext2>[a-zA-Z0-9_-]+)\s+files?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Attempts to parse natural language input into a structured SemanticIntent.
    /// </summary>
    public static SemanticIntent? Parse(string input, ShellContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var lower = input.ToLowerInvariant().Trim();
        var intent = new SemanticIntent();

        // 1. Detect Action
        if (ActionKillRegex.IsMatch(lower)) intent.Action = SemanticAction.TerminateOrKill;
        else if (ActionRestartRegex.IsMatch(lower)) intent.Action = SemanticAction.RestartOrBounce;
        else if (ActionDeleteRegex.IsMatch(lower)) intent.Action = SemanticAction.RemoveOrDelete;
        else if (ActionFindRegex.IsMatch(lower)) intent.Action = SemanticAction.FindOrList;
        else intent.Action = SemanticAction.FindOrList;

        // 2. Detect Entity Type
        if (lower.Contains("port") || PortRegex.IsMatch(lower))
        {
            intent.TargetType = EntityType.PortsOrConnections;
            var m = PortRegex.Match(lower);
            if (m.Success)
            {
                var pStr = m.Groups["port"].Success ? m.Groups["port"].Value : m.Groups["port2"].Value;
                if (int.TryParse(pStr, out int p)) intent.TargetPort = p;
            }

            var hostMatch = RemoteHostRegex.Match(lower);
            if (hostMatch.Success)
            {
                intent.TargetName = hostMatch.Groups["host"].Value;
            }
        }
        else if (lower.Contains("process") || lower.Contains("processes") || lower.Contains("cpu") || lower.Contains("ram") || lower.Contains("memory") || PidRegex.IsMatch(lower) || lower.Contains("hog"))
        {
            intent.TargetType = EntityType.Processes;
            var pidMatch = PidRegex.Match(lower);
            if (pidMatch.Success && int.TryParse(pidMatch.Groups["pid"].Value, out int pid))
            {
                intent.TargetPid = pid;
            }
        }
        else if (lower.Contains("service") || lower.Contains("services") || lower.Contains("daemon"))
        {
            intent.TargetType = EntityType.Services;
        }
        else if (lower.Contains("file") || lower.Contains("files") || lower.Contains("folder") || lower.Contains("directory") || lower.Contains("videos") || lower.Contains("images") || lower.Contains("logs") || lower.Contains("docs") || ExtensionRegex.IsMatch(lower))
        {
            intent.TargetType = EntityType.FilesOrFolders;
        }
        else if (lower.Contains("disk") || lower.Contains("drive") || lower.Contains("storage") || lower.Contains("partition") || lower.Contains("volume") || lower.Contains("free space"))
        {
            intent.TargetType = EntityType.DisksOrVolumes;
        }
        else if (lower.Contains("ip") || lower.Contains("ip address") || lower.Contains("dns") || lower.Contains("ping") || lower.Contains("wifi") || lower.Contains("network"))
        {
            intent.TargetType = EntityType.SystemInformation;
        }

        // 3. Detect Limits & Sorting
        var limitMatch = TopLimitRegex.Match(lower);
        if (limitMatch.Success)
        {
            var numStr = limitMatch.Groups["num"].Success ? limitMatch.Groups["num"].Value : limitMatch.Groups["num2"].Value;
            if (int.TryParse(numStr, out int num))
            {
                intent.LimitCount = num;
            }
        }

        if (lower.Contains("by cpu") || lower.Contains("highest cpu") || lower.Contains("most cpu"))
        {
            intent.SortBy = "CPU";
            intent.SortDescending = true;
        }
        else if (lower.Contains("by memory") || lower.Contains("by ram") || lower.Contains("highest ram") || lower.Contains("most memory") || lower.Contains("memory hogs") || lower.Contains("ram hogs") || lower.Contains("heaviest ram"))
        {
            intent.SortBy = "WorkingSet64";
            intent.SortDescending = true;
        }
        else if (lower.Contains("by size") || lower.Contains("largest") || lower.Contains("biggest"))
        {
            intent.SortBy = "Length";
            intent.SortDescending = true;
        }
        else if (lower.Contains("by date") || lower.Contains("newest") || lower.Contains("recent") || lower.Contains("latest"))
        {
            intent.SortBy = "LastWriteTime";
            intent.SortDescending = true;
        }

        if (lower.Contains("recursive") || lower.Contains("in subfolders") || lower.Contains("all folders"))
        {
            intent.Recursive = true;
        }

        // 4. File extension detection
        var extMatch = ExtensionRegex.Match(lower);
        if (extMatch.Success)
        {
            var ext = extMatch.Groups["ext"].Success ? extMatch.Groups["ext"].Value : extMatch.Groups["ext2"].Value;
            if (!ExcludedExtensionWords.Contains(ext))
            {
                intent.FileExtension = ext.TrimStart('.');
            }
        }

        // 5. Target folder / path detection
        var folderMatch = FolderRegex.Match(lower);
        if (folderMatch.Success)
        {
            var f = folderMatch.Groups["folder"].Value;
            if (!ExcludedFolderWords.Contains(f))
            {
                intent.TargetPath = f;
            }
        }

        return intent;
    }

    /// <summary>
    /// Translates a SemanticIntent into an executable, safe TranslationResult.
    /// </summary>
    public static TranslationResult? GenerateTranslation(SemanticIntent intent, string rawInput, ShellContext? context = null)
    {
        if (intent == null) return null;

        // 1. Process Operations
        if (intent.TargetType == EntityType.Processes)
        {
            if (intent.Action == SemanticAction.TerminateOrKill)
            {
                if (intent.TargetPid.HasValue)
                {
                    return TranslationResult.CreateSuccess(
                        rawInput,
                        $"Stop-Process -Id {intent.TargetPid.Value} -Force",
                        $"Terminates process with PID {intent.TargetPid.Value}.",
                        RiskLevel.High,
                        IntentCategory.ProcessManagement
                    );
                }

                if (!string.IsNullOrWhiteSpace(intent.TargetName))
                {
                    return TranslationResult.CreateSuccess(
                        rawInput,
                        $"Stop-Process -Name '{intent.TargetName}' -Force",
                        $"Terminates all running instances of '{intent.TargetName}'.",
                        RiskLevel.High,
                        IntentCategory.ProcessManagement
                    );
                }
            }

            // Process listing / sorting
            string? sortCol = intent.SortBy ?? (intent.LimitCount.HasValue ? "CPU" : null);
            var limit = intent.LimitCount ?? 15;

            string script;
            if (!string.IsNullOrEmpty(sortCol))
            {
                script = $"Get-Process | Sort-Object {sortCol} -Descending | Select-Object -First {limit} Id, ProcessName, @{{Name='RAM_MB';Expression={{[math]::Round($_.WorkingSet64/1MB,1)}}}}, CPU";
            }
            else
            {
                script = $"Get-Process | Select-Object -First {limit} Id, ProcessName, @{{Name='RAM_MB';Expression={{[math]::Round($_.WorkingSet64/1MB,1)}}}}, CPU";
            }

            return TranslationResult.CreateSuccess(
                rawInput,
                script,
                $"Retrieves processes sorted by {sortCol ?? "default"}.",
                RiskLevel.Safe,
                IntentCategory.ProcessManagement
            );
        }

        // 2. Port / Network Operations
        if (intent.TargetType == EntityType.PortsOrConnections)
        {
            if (intent.TargetPort.HasValue)
            {
                if (!string.IsNullOrEmpty(intent.TargetName))
                {
                    var script = $"Test-NetConnection -ComputerName '{intent.TargetName}' -Port {intent.TargetPort.Value}";
                    return TranslationResult.CreateSuccess(
                        rawInput,
                        script,
                        $"Tests if port {intent.TargetPort.Value} is open on '{intent.TargetName}'.",
                        RiskLevel.Safe,
                        IntentCategory.Network
                    );
                }

                if (intent.Action == SemanticAction.TerminateOrKill)
                {
                    var script = $"$conn = Get-NetTCPConnection -LocalPort {intent.TargetPort.Value} -ErrorAction SilentlyContinue; if ($conn) {{ Stop-Process -Id $conn.OwningProcess -Force; Write-Host \"Killed process listening on port {intent.TargetPort.Value}\" }} else {{ Write-Host \"No active process found on port {intent.TargetPort.Value}\" }}";
                    return TranslationResult.CreateSuccess(
                        rawInput,
                        script,
                        $"Kills the process listening on port {intent.TargetPort.Value}.",
                        RiskLevel.High,
                        IntentCategory.ProcessManagement
                    );
                }

                return TranslationResult.CreateSuccess(
                    rawInput,
                    $"Get-NetTCPConnection -LocalPort {intent.TargetPort.Value} -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, State, OwningProcess",
                    $"Checks which process is using port {intent.TargetPort.Value}.",
                    RiskLevel.Safe,
                    IntentCategory.Network
                );
            }
        }

        // 3. File Operations
        if (intent.TargetType == EntityType.FilesOrFolders)
        {
            var path = intent.TargetPath ?? ".";
            var recurse = intent.Recursive ? " -Recurse" : "";
            var filter = !string.IsNullOrEmpty(intent.FileExtension) ? $" -Filter '*.{intent.FileExtension}'" : "";

            if (intent.Action == SemanticAction.RemoveOrDelete)
            {
                var deleteScript = $"Get-ChildItem -Path '{path}'{filter}{recurse} -File | Remove-Item -Force";
                return TranslationResult.CreateSuccess(
                    rawInput,
                    deleteScript,
                    $"Deletes files matching filter '{intent.FileExtension ?? "*"}'.",
                    RiskLevel.High,
                    IntentCategory.FileSystem
                );
            }

            string script;
            if (!string.IsNullOrEmpty(intent.SortBy))
            {
                var limitClause = intent.LimitCount.HasValue ? $" | Select-Object -First {intent.LimitCount.Value}" : "";
                script = $"Get-ChildItem -Path '{path}'{filter}{recurse} -File | Sort-Object {intent.SortBy} -Descending{limitClause} | Select-Object Name, @{{Name='Size_MB';Expression={{[math]::Round($_.Length/1MB,2)}}}}, LastWriteTime";
            }
            else
            {
                var limitClause = intent.LimitCount.HasValue ? $" | Select-Object -First {intent.LimitCount.Value}" : "";
                script = $"Get-ChildItem -Path '{path}'{filter}{recurse}{limitClause}";
            }

            return TranslationResult.CreateSuccess(
                rawInput,
                script,
                $"Finds files in '{path}' {filter}.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
        }

        // 4. Disks and Storage
        if (intent.TargetType == EntityType.DisksOrVolumes)
        {
            return TranslationResult.CreateSuccess(
                rawInput,
                "Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' } | Select-Object DriveLetter, FileSystemLabel, @{Name='Free_GB';Expression={[math]::Round($_.SizeRemaining/1GB,2)}}, @{Name='Total_GB';Expression={[math]::Round($_.Size/1GB,2)}}",
                "Retrieves disk volume free space.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
        }

        return null;
    }
}
