using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Detects ambiguous or underspecified natural language commands and generates clarifying questions with structured options.
/// Prevents guessing or executing wrong commands by asking natural follow-up questions.
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

    private static readonly Regex DeleteFilesUnderspecifiedRegex = new(
        @"^(?:delete|remove|clean|wipe|purge|erase)\s+(?:all\s+)?files?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServiceUnderspecifiedRegex = new(
        @"^(?:stop|restart|start|check)\s+services?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProcessUnderspecifiedRegex = new(
        @"^(?:kill|terminate|stop|close|end)\s+(?:the\s+)?(?:process|program|app|task)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NetworkUnderspecifiedRegex = new(
        @"^(?:check|test|diagnose|ping)\s+(?:connection|network|internet|wifi)$|^ping$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildUnderspecifiedRegex = new(
        @"^(?:build|build\s+project|compile|run\s+build)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortUnderspecifiedRegex = new(
        @"^(?:check|test|scan|inspect)\s+ports?$|^check\s+port$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DanglingPronounRegex = new(
        @"^(?:kill|terminate|close|delete|remove|open)\s+(?:it|that|them|this)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAmbiguous(string input, ShellContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var lower = input.ToLowerInvariant().Trim();
        if (CleanTempRegex.IsMatch(lower)
            || KillProcessFamilyRegex.IsMatch(lower)
            || BackupProjectRegex.IsMatch(lower)
            || ResetWorkspaceRegex.IsMatch(lower)
            || DeleteLogsRegex.IsMatch(lower)
            || DeleteFilesUnderspecifiedRegex.IsMatch(lower)
            || ServiceUnderspecifiedRegex.IsMatch(lower)
            || ProcessUnderspecifiedRegex.IsMatch(lower)
            || NetworkUnderspecifiedRegex.IsMatch(lower)
            || BuildUnderspecifiedRegex.IsMatch(lower)
            || PortUnderspecifiedRegex.IsMatch(lower))
        {
            return true;
        }

        if (DanglingPronounRegex.IsMatch(lower))
        {
            // If no previous targets exist in memory or last output
            if (context == null || (context.Memory.LastReferencedProcessIds.Count == 0 &&
                                    context.Memory.LastReferencedProcessNames.Count == 0 &&
                                    context.Memory.LastReferencedPaths.Count == 0 &&
                                    context.LastOutputObjects.Count == 0))
            {
                return true;
            }
        }

        return false;
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
                Question = "What would you like to clean?",
                DisambiguationType = "CleanTemp",
                OriginalConfidence = 0.50
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
                Question = $"Multiple '{proc}' instances may be active. How would you like to proceed?",
                DisambiguationType = "ProcessFamily",
                OriginalConfidence = 0.60
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
                Question = "How would you like to back up this project?",
                DisambiguationType = "BackupProject",
                OriginalConfidence = 0.55
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
                Question = "What would you like to reset?",
                DisambiguationType = "ResetWorkspace",
                OriginalConfidence = 0.40
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
                Question = "Where would you like to delete log files from?",
                DisambiguationType = "DeleteLogs",
                OriginalConfidence = 0.55
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

        // 6. Underspecified File Deletion
        if (DeleteFilesUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "What files would you like to delete, and from where?",
                DisambiguationType = "DeleteFiles",
                OriginalConfidence = 0.35
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Temporary files in $env:TEMP",
                Description = "Safely cleans cache and temporary files without touching projects.",
                PowerShellScript = "Get-ChildItem -Path $env:TEMP -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; Write-Host \"Cleaned user temp directory.\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Build output directories (bin / obj / node_modules)",
                Description = "Deletes compiled artifacts in the current project.",
                PowerShellScript = "Get-ChildItem -Path '.' -Include bin,obj,node_modules -Recurse -Directory | Remove-Item -Recurse -Force; Write-Host \"Cleaned build artifacts.\"",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.FileSystem
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "All files in current folder (*.*)",
                Description = "Permanently removes all files in current folder.",
                PowerShellScript = "Remove-Item -Path '.*' -Force",
                Risk = RiskLevel.Critical,
                Category = IntentCategory.FileSystem
            });

            return req;
        }

        // 7. Underspecified Service Action
        if (ServiceUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Which Windows service would you like to inspect or manage?",
                DisambiguationType = "ServiceAction",
                OriginalConfidence = 0.40
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "List all running Windows services",
                Description = "Shows all currently active services and their status.",
                PowerShellScript = "Get-Service | Where-Object Status -eq 'Running' | Select-Object -First 25 Name, DisplayName, Status",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.WindowsServices
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Windows Update Service (wuauserv)",
                Description = "Manages the Windows Update background service.",
                PowerShellScript = "Get-Service -Name 'wuauserv'",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.WindowsServices
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "Print Spooler Service (Spooler)",
                Description = "Manages the Windows print spooler service.",
                PowerShellScript = "Get-Service -Name 'Spooler'",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.WindowsServices
            });

            return req;
        }

        // 8. Underspecified Process Action
        if (ProcessUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Which process would you like to target?",
                DisambiguationType = "ProcessAction",
                OriginalConfidence = 0.40
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Process consuming the most CPU",
                Description = "Targets the single highest CPU-consuming application.",
                PowerShellScript = "Get-Process | Where-Object { $_.CPU -gt 0 } | Sort-Object CPU -Descending | Select-Object -First 1 Id, ProcessName, CPU",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.ProcessManagement
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Process consuming the most RAM",
                Description = "Targets the single heaviest memory-consuming application.",
                PowerShellScript = "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 1 Id, ProcessName, @{Name='RAM_MB';Expression={[math]::Round($_.WorkingSet64/1MB,1)}}",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.ProcessManagement
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "List top 15 running processes",
                Description = "Displays active processes with PIDs to inspect first.",
                PowerShellScript = "Get-Process | Sort-Object CPU -Descending | Select-Object -First 15 Id, ProcessName, CPU, @{Name='RAM_MB';Expression={[math]::Round($_.WorkingSet64/1MB,1)}}",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.ProcessManagement
            });

            return req;
        }

        // 9. Underspecified Network Diagnostics
        if (NetworkUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Where would you like to test network connectivity to?",
                DisambiguationType = "NetworkDiagnostic",
                OriginalConfidence = 0.50
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "Public Internet (Google DNS 8.8.8.8)",
                Description = "Tests outbound internet connectivity and latency.",
                PowerShellScript = "Test-NetConnection -ComputerName 8.8.8.8 | Select-Object ComputerName, PingSucceeded, RoundTripTime",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Network
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Active Network Adapters & IP Configuration",
                Description = "Displays IPv4, gateway, and DNS configuration for all adapters.",
                PowerShellScript = "Get-NetIPConfiguration | Select-Object InterfaceAlias, IPv4Address, IPv4DefaultGateway, DNSServer",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Network
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "DNS Resolution (google.com)",
                Description = "Resolves domain name to verify DNS operation.",
                PowerShellScript = "Resolve-DnsName -Name 'google.com' -ErrorAction SilentlyContinue | Select-Object Name, Type, IPAddress",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Network
            });

            return req;
        }

        // 10. Underspecified Build / Project Action
        if (BuildUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Which project build tool would you like to use?",
                DisambiguationType = "BuildProject",
                OriginalConfidence = 0.50
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = ".NET Solution / Project (dotnet build)",
                Description = "Compiles the active C# .NET solution.",
                PowerShellScript = "dotnet build",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.CustomScript
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Node.js / NPM Project (npm run build)",
                Description = "Executes the NPM build script.",
                PowerShellScript = "npm run build",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.CustomScript
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 3,
                Label = "Rust Cargo Project (cargo build)",
                Description = "Compiles Rust cargo crate.",
                PowerShellScript = "cargo build",
                Risk = RiskLevel.Moderate,
                Category = IntentCategory.CustomScript
            });

            return req;
        }

        // 11. Underspecified Port Check
        if (PortUnderspecifiedRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "Which port or connection check would you like to perform?",
                DisambiguationType = "PortCheck",
                OriginalConfidence = 0.50
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "List all active listening TCP ports",
                Description = "Displays all ports currently bound and listening for incoming connections.",
                PowerShellScript = "Get-NetTCPConnection -State Listen | Select-Object LocalAddress, LocalPort, OwningProcess | Sort-Object LocalPort",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Network
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "Check standard local web server ports (80, 443, 3000, 5000, 8080)",
                Description = "Filters for common HTTP/HTTPS development ports.",
                PowerShellScript = "Get-NetTCPConnection -LocalPort 80, 443, 3000, 5000, 8080 -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, State, OwningProcess",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.Network
            });

            return req;
        }

        // 12. Dangling Pronoun without target
        if (DanglingPronounRegex.IsMatch(lower))
        {
            var req = new ClarificationRequest
            {
                OriginalInput = input,
                Question = "What target would you like to act on? There is no active item in context.",
                DisambiguationType = "DanglingReference",
                OriginalConfidence = 0.20
            };

            req.Options.Add(new ClarificationOption
            {
                Index = 1,
                Label = "List top running processes to select a PID",
                Description = "Shows active processes to pick a target ID.",
                PowerShellScript = "Get-Process | Select-Object -First 15 Id, ProcessName, CPU",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.ProcessManagement
            });

            req.Options.Add(new ClarificationOption
            {
                Index = 2,
                Label = "List files in current directory to select a file",
                Description = "Shows files in current directory.",
                PowerShellScript = "Get-ChildItem",
                Risk = RiskLevel.Safe,
                Category = IntentCategory.FileSystem
            });

            return req;
        }

        return null;
    }
}
