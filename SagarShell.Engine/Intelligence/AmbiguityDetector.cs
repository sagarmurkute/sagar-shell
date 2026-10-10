using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Detects ambiguous or underspecified natural language commands and generates clarifying questions with structured options.
/// </summary>
public static class AmbiguityDetector
{
    private static readonly Regex CleanTempRegex = new(
        @"^(?:clean|clear|delete|empty|purge)\s+(?:the\s+)?temp(?:\s+files?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex KillProcessFamilyRegex = new(
        @"^(?:kill|terminate|stop)\s+(?<proc>python|node|chrome|code|java|dotnet|powershell|msedge)(?:\s+processes?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BackupProjectRegex = new(
        @"^(?:backup|save|archive)\s+(?:this\s+)?(?:project|repo|folder|directory|code)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResetWorkspaceRegex = new(
        @"^(?:reset|clean\s+workspace|reset\s+project|reset\s+all)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeleteLogsRegex = new(
        @"^(?:delete|remove|clean|clear|purge)\s+(?:all\s+)?logs?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAmbiguous(string input, ShellContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var lower = input.ToLowerInvariant().Trim();
        return CleanTempRegex.IsMatch(lower)
            || KillProcessFamilyRegex.IsMatch(lower)
            || BackupProjectRegex.IsMatch(lower)
            || ResetWorkspaceRegex.IsMatch(lower)
            || DeleteLogsRegex.IsMatch(lower);
    }

    public static ClarificationRequest? DetectAmbiguity(string input, ShellContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var lower = input.ToLowerInvariant().Trim();

        // 1. Clean Temp
        if (CleanTempRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "What would you like to clean?"
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "User Temporary Files ($env:TEMP)",
                Description = "Deletes temporary cache files in your user profile temp folder.",
                PowerShellScript = "Get-ChildItem -Path $env:TEMP -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Write-Host \"Cleaned user temp directory.\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Windows Recycle Bin",
                Description = "Permanently empties the Windows Recycle Bin.",
                PowerShellScript = "Clear-RecycleBin -Force -ErrorAction SilentlyContinue; Write-Host \"Recycle bin emptied.\"",
                Risk = RiskLevel.Critical,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "Root C:\\Temp Directory",
                Description = "Deletes all items inside C:\\Temp.",
                PowerShellScript = "Remove-Item -Path 'C:\\Temp\\*' -Recurse -Force -ErrorAction SilentlyContinue; Write-Host \"Cleaned C:\\Temp.\"",
                Risk = RiskLevel.Critical,
                Category = IntentCategory.FileSystem,
                RequiresElevation = true
            });

            return req;
        }

        // 2. Kill Process Family (e.g. Python, Node, Chrome)
        var killMatch = KillProcessFamilyRegex.Match(lower);
        if (killMatch.Success)
        {
            var proc = killMatch.Groups["proc"].Value;
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = $"Multiple '{proc}' instances may be active. How would you like to proceed?"
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = $"List all active '{proc}' processes first",
                Description = $"Displays all running '{proc}' processes with PIDs and memory usage before terminating.",
                PowerShellScript = $"Get-Process -Name '{proc}' -ErrorAction SilentlyContinue | Select-Object Id, Name, @{{Name='RAM_MB';Expression={{[math]::Round($_.WorkingSet64/1MB,1)}}}}, CPU",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.ProcessManagement
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = $"Force kill ALL '{proc}' processes immediately",
                Description = $"Terminates every running instance of '{proc}' without further prompts.",
                PowerShellScript = $"Stop-Process -Name '{proc}' -Force; Write-Host \"Terminated all {proc} processes.\"",
                Risk = RiskLevel.High,
                Category = IntentCategory.ProcessManagement
            });

            return req;
        }

        // 3. Backup Project
        if (BackupProjectRegex.IsMatch(lower))
        {
            var folderName = System.IO.Path.GetFileName(context?.CurrentDirectory ?? "Project");
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "How would you like to back up this project?"
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Create a timestamped ZIP archive",
                Description = $"Compresses the current folder into '{folderName}_backup.zip'.",
                PowerShellScript = $"Compress-Archive -Path '.' -DestinationPath '{folderName}_backup.zip' -Force; Write-Host \"Created {folderName}_backup.zip\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Create a Git checkpoint commit",
                Description = "Stages all changes and creates a local Git commit with timestamp.",
                PowerShellScript = "git add -A; git commit -m \"Backup checkpoint - $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.GitOperations
            });

            return req;
        }

        // 4. Reset Workspace
        if (ResetWorkspaceRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "What would you like to reset?"
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Reset Git Working Tree (Discard uncommitted changes)",
                Description = "Runs 'git reset --hard HEAD' and 'git clean -fd' to restore clean repository state.",
                PowerShellScript = "git reset --hard HEAD; git clean -fd",
                Risk = RiskLevel.Critical,
                Category = IntentCategory.GitOperations
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Clear Terminal Screen & Reset Session State",
                Description = "Clears screen and resets in-memory variables and history.",
                PowerShellScript = "Clear-Host",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Navigation
            });

            return req;
        }

        // 5. Delete Logs
        if (DeleteLogsRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Where would you like to delete log files from?"
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Current directory only (*.log)",
                Description = "Deletes *.log files in current folder only (non-recursive).",
                PowerShellScript = "Remove-Item -Path '*.log' -Force; Write-Host \"Deleted log files in current directory.\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Recursive (All subdirectories)",
                Description = "Deletes *.log files across all child folders recursively.",
                PowerShellScript = "Get-ChildItem -Path '.' -Filter '*.log' -Recurse -File | Remove-Item -Force; Write-Host \"Deleted all log files recursively.\"",
                Risk = RiskLevel.High,
                Category = IntentCategory.FileSystem
            });

            return req;
        }

        return null;
    }
}
