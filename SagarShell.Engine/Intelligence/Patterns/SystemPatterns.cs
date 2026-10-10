using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles system hardware queries, RAM/CPU stats, disk space, battery, environment variables, and power states.
/// </summary>
public class SystemPatterns : ICommandPattern
{
    public int Priority => 80;

    // System info / Specs: "system info", "specs", "pc specs", "os version"
    private static readonly Regex SystemInfoRegex = new(
        @"\b(?:system\s*info|system\s+specs|hardware\s+specs|specs|windows\s+version|os\s+version|computer\s+info)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // RAM usage: "how much free ram", "show memory usage", "ram info"
    private static readonly Regex RamRegex = new(
        @"\b(?:free\s+ram|ram\s+usage|memory\s+usage|available\s+memory|how\s+much\s+ram)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // CPU stats: "cpu usage", "check cpu"
    private static readonly Regex CpuRegex = new(
        @"\b(?:cpu\s+usage|processor\s+usage|cpu\s+load)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Uptime: "system uptime", "how long has this pc been on", "uptime"
    private static readonly Regex UptimeRegex = new(
        @"\b(?:uptime|system\s+uptime|how\s+long\s+has\s+(?:this\s+pc|the\s+computer)\s+been\s+on|boot\s+time)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Battery: "battery status", "check battery", "battery health", "battery level"
    private static readonly Regex BatteryRegex = new(
        @"\b(?:battery\s+status|battery\s+health|battery\s+level|battery\s+percentage|check\s+battery)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Disk space: "show disk space", "how much free space on c drive", "disk free", "drive info"
    private static readonly Regex DiskSpaceRegex = new(
        @"\b(?:disk\s+space|free\s+disk|hard\s+drive\s+space|drives?|disk\s+usage|free\s+space(?:\s+on\s+[A-Za-z](?:\s+drive)?)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Environment variables: "show env variables", "get path env", "show all environment variables"
    private static readonly Regex EnvVarListRegex = new(
        @"\b(?:(?:show|list|get|display)\s+(?:all\s+)?env(?:ironment)?\s+vars?|environment\s+variables?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GetEnvVarRegex = new(
        @"(?:get|show|view|print)\s+(?:the\s+)?(?:env(?:ironment)?\s+var(?:iable)?\s+)?['""]?\$?(?:env:)?(?<var>[a-zA-Z0-9_]+)['""]?\s*(?:variable)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SetEnvVarRegex = new(
        @"(?:set|export)\s+(?:env(?:ironment)?\s+var(?:iable)?\s+)?['""]?(?<var>[a-zA-Z0-9_]+)['""]?\s+(?:to|=)\s+['""]?(?<val>[^'""]+)['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Power controls: "shutdown", "restart", "sleep", "lock pc"
    private static readonly Regex LockScreenRegex = new(
        @"\b(?:lock\s+(?:computer|pc|screen)|lock)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SleepRegex = new(
        @"\b(?:put\s+(?:pc|computer)\s+to\s+sleep|sleep\s+computer|sleep)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RestartRegex = new(
        @"\b(?:restart\s+(?:pc|computer|system|windows)|reboot)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShutdownRegex = new(
        @"(?:shutdown|turn\s+off)\s+(?:the\s+)?(?:pc|computer|system)(?:\s+in\s+(?<time>\d+)\s*(?<unit>seconds?|minutes?|hours?))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AbortShutdownRegex = new(
        @"\b(?:cancel|abort|stop)\s+shutdown\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "specs" or "uptime" or "battery" or "drives" or "df"
            || SystemInfoRegex.IsMatch(input)
            || RamRegex.IsMatch(input)
            || CpuRegex.IsMatch(input)
            || UptimeRegex.IsMatch(input)
            || BatteryRegex.IsMatch(input)
            || DiskSpaceRegex.IsMatch(input)
            || EnvVarListRegex.IsMatch(input)
            || GetEnvVarRegex.IsMatch(input)
            || SetEnvVarRegex.IsMatch(input)
            || LockScreenRegex.IsMatch(input)
            || SleepRegex.IsMatch(input)
            || RestartRegex.IsMatch(input)
            || ShutdownRegex.IsMatch(input)
            || AbortShutdownRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. Lock Screen
        if (LockScreenRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "rundll32.exe user32.dll,LockWorkStation",
                "Locks the Windows user session.",
                RiskLevel.Low,
                IntentCategory.PowerControl
            );
        }

        // 2. Sleep
        if (SleepRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "rundll32.exe powrprof.dll,SetSuspendState 0,1,0",
                "Suspends system state into sleep mode.",
                RiskLevel.Moderate,
                IntentCategory.PowerControl
            );
        }

        // 3. Abort Shutdown
        if (AbortShutdownRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "shutdown /a",
                "Aborts any pending scheduled Windows shutdown.",
                RiskLevel.Low,
                IntentCategory.PowerControl
            );
        }

        // 4. Shutdown
        var shutdownMatch = ShutdownRegex.Match(input);
        if (shutdownMatch.Success)
        {
            int seconds = 0;
            if (shutdownMatch.Groups["time"].Success)
            {
                var time = int.Parse(shutdownMatch.Groups["time"].Value);
                var unit = shutdownMatch.Groups["unit"].Value.ToLowerInvariant();
                seconds = unit.StartsWith("minute") ? time * 60 : (unit.StartsWith("hour") ? time * 3600 : time);
            }

            var ps = $"shutdown /s /t {seconds}";
            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Initiates Windows shutdown" + (seconds > 0 ? $" in {seconds} seconds." : " immediately."),
                RiskLevel.Critical,
                IntentCategory.PowerControl
            );
        }

        // 5. Restart
        if (RestartRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "shutdown /r /t 0",
                "Reboots the computer.",
                RiskLevel.Critical,
                IntentCategory.PowerControl
            );
        }

        // 6. Uptime
        if (UptimeRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "((Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime) | Select-Object Days, Hours, Minutes, Seconds, TotalHours",
                "Calculates time elapsed since the last system boot.",
                RiskLevel.Safe,
                IntentCategory.SystemInformation
            );
        }

        // 7. Battery
        if (BatteryRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-CimInstance -ClassName Win32_Battery | Select-Object Name, EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime",
                "Queries the battery status and remaining charge percentage.",
                RiskLevel.Safe,
                IntentCategory.SystemInformation
            );
        }

        // 8. RAM / Memory
        if (RamRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-CimInstance Win32_OperatingSystem | Select-Object @{Name='TotalGB';Expression={[math]::Round($_.TotalVisibleMemorySize/1MB,2)}}, @{Name='FreeGB';Expression={[math]::Round($_.FreePhysicalMemory/1MB,2)}}, @{Name='UsedGB';Expression={[math]::Round(($_.TotalVisibleMemorySize-$_.FreePhysicalMemory)/1MB,2)}}, @{Name='PercentFree';Expression={[math]::Round(($_.FreePhysicalMemory/$_.TotalVisibleMemorySize)*100,1)}}",
                "Displays detailed physical RAM total, used, and free memory statistics.",
                RiskLevel.Safe,
                IntentCategory.SystemInformation
            );
        }

        // 9. CPU Load
        if (CpuRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors, LoadPercentage, MaxClockSpeed",
                "Queries current CPU load and processor specification.",
                RiskLevel.Safe,
                IntentCategory.SystemInformation
            );
        }

        // 10. Disk Space
        if (DiskSpaceRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' -and $_.Size -gt 0 } | Select-Object DriveLetter, FileSystemLabel, @{Name='SizeGB';Expression={[math]::Round($_.Size/1GB,2)}}, @{Name='FreeGB';Expression={[math]::Round($_.SizeRemaining/1GB,2)}}, @{Name='PercentFree';Expression={[math]::Round(($_.SizeRemaining/$_.Size)*100,1)}}",
                "Lists storage capacity and free disk space across all fixed partitions.",
                RiskLevel.Safe,
                IntentCategory.SystemInformation
            );
        }

        // 11. Set Env Var
        var setEnvMatch = SetEnvVarRegex.Match(input);
        if (setEnvMatch.Success)
        {
            var varName = setEnvMatch.Groups["var"].Value.Trim();
            var varVal = setEnvMatch.Groups["val"].Value.Trim();
            var ps = $"[System.Environment]::SetEnvironmentVariable('{varName}', '{varVal.Replace("'", "''")}', 'User'); Write-Host \"Set User environment variable {varName}={varVal}\"";

            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Persists environment variable '{varName}' with value '{varVal}' for the current user.",
                RiskLevel.Moderate,
                IntentCategory.EnvironmentVariables
            );
        }

        // 12. Get specific Env Var
        var getEnvMatch = GetEnvVarRegex.Match(input);
        if (getEnvMatch.Success && !lower.Contains("all") && !lower.Contains("list"))
        {
            var varName = getEnvMatch.Groups["var"].Value.Trim();
            var ps = $"$env:{varName}";
            return TranslationResult.CreateSuccess(
                input,
                ps,
                $"Retrieves the current value of environment variable '{varName}'.",
                RiskLevel.Safe,
                IntentCategory.EnvironmentVariables
            );
        }

        // 13. List all Env Vars
        if (EnvVarListRegex.IsMatch(input) || lower.Contains("env"))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-ChildItem env: | Sort-Object Name | Select-Object Name, Value",
                "Lists all session environment variables.",
                RiskLevel.Safe,
                IntentCategory.EnvironmentVariables
            );
        }

        // 14. Full System Info
        return TranslationResult.CreateSuccess(
            input,
            "Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsArchitecture, CsName, CsManufacturer, CsModel, CsProcessors, CsTotalPhysicalMemory",
            "Queries Windows OS version, hardware model, architecture, and memory specs.",
            RiskLevel.Safe,
            IntentCategory.SystemInformation
        );
    }
}
