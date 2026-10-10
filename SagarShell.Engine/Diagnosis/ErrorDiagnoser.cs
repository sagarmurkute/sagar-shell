using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// Diagnoses PowerShell and Windows command failures into plain-English explanations with validated recovery suggestions.
/// </summary>
public static class ErrorDiagnoser
{
    private static readonly Regex AccessDeniedRegex = new(
        @"(?:UnauthorizedAccessException|Access\s+is\s+denied|PermissionDenied|requires\s+elevation|Administrator\s+rights|run\s+as\s+administrator)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PathNotFoundRegex = new(
        @"(?:ItemNotFoundException|PathNotFound|Cannot\s+find\s+path|does\s+not\s+exist|DirectoryNotFoundException|FileNotFoundException|No\s+such\s+file\s+or\s+directory)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommandNotFoundRegex = new(
        @"(?:CommandNotFoundException|is\s+not\s+recognized\s+as\s+the\s+name|is\s+not\s+recognized\s+as\s+an\s+internal\s+or\s+external|command\s+not\s+found|The\s+term\s+'.*'\s+is\s+not\s+recognized)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProcessNotFoundRegex = new(
        @"(?:ProcessCommandException|Cannot\s+find\s+a\s+process\s+with\s+the\s+(?:name|process\s+identifier)|No\s+process\s+with\s+ID)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ServiceNotFoundRegex = new(
        @"(?:ServiceCommandException|Cannot\s+find\s+any\s+service\s+with\s+service\s+name|Service\s+'.*'\s+was\s+not\s+found)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResourceLockedRegex = new(
        @"(?:The\s+process\s+cannot\s+access\s+the\s+file|being\s+used\s+by\s+another\s+process|file\s+is\s+locked|SharingViolation)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortInUseRegex = new(
        @"(?:AddressAlreadyInUse|Only\s+one\s+usage\s+of\s+each\s+socket\s+address|address\s+already\s+in\s+use|port\s+already\s+in\s+use)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NetworkFailureRegex = new(
        @"(?:WebException|HttpRequestException|Could\s+not\s+resolve\s+host|No\s+such\s+host\s+is\s+known|Connection\s+refused|timed\s+out|Network\s+is\s+unreachable)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DiskFullRegex = new(
        @"(?:There\s+is\s+not\s+enough\s+space\s+on\s+the\s+disk|DiskFull|quota\s+exceeded)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ScriptDisabledRegex = new(
        @"(?:running\s+scripts\s+is\s+disabled\s+on\s+this\s+system|PSSecurityException|Execution_Policies)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ErrorDiagnosis Diagnose(ExecutionResult result, TranslationResult? translation = null, ShellContext? context = null)
    {
        var rawError = !string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardError
            : result.StandardOutput;

        return Diagnose(rawError, result.ExitCode, translation, context);
    }

    public static ErrorDiagnosis Diagnose(string rawError, int exitCode = 1, TranslationResult? translation = null, ShellContext? context = null)
    {
        var diag = new ErrorDiagnosis
        {
            RawErrorMessage = rawError ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(rawError))
        {
            diag.Category = ErrorCategory.GeneralFailure;
            diag.PlainEnglishSummary = "Operation returned a non-zero exit code without detailed error output.";
            diag.LikelyCause = "The executable exited with an error state.";
            return diag;
        }

        // 1. Script Execution Disabled
        if (ScriptDisabledRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.ScriptExecutionDisabled;
            diag.PlainEnglishSummary = "Script Execution Blocked: PowerShell policy disables running script files.";
            diag.LikelyCause = "Windows execution policy on this user profile is set to Restricted.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Enable RemoteSigned script execution policy for the current user.",
                RemediationScript = "Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force",
                RetryScript = translation?.PowerShellScript,
                Risk = RiskLevel.Moderate,
                RequiresElevation = false,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 2. Access Denied / Elevation Required
        if (AccessDeniedRegex.IsMatch(rawError) || (translation != null && translation.RequiresElevation && context != null && !context.IsElevated))
        {
            diag.Category = ErrorCategory.AccessDenied;
            diag.PlainEnglishSummary = "Access Denied: You do not have permission to execute this operation.";
            diag.LikelyCause = context != null && !context.IsElevated
                ? "This command requires Administrator elevation, but Sagar Shell is running as a standard user."
                : "The targeted file, registry key, or resource is protected or owned by another user.";

            string? scriptToElevate = translation?.PowerShellScript;
            string? elevateScript = !string.IsNullOrEmpty(scriptToElevate)
                ? $"Start-Process powershell -Verb RunAs -ArgumentList '-NoExit', '-Command', {NaturalPathResolver.EscapeForPowerShell(scriptToElevate)}"
                : null;

            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Launch an elevated Administrator PowerShell prompt to complete this operation.",
                RemediationScript = elevateScript,
                RetryScript = translation?.PowerShellScript,
                Risk = RiskLevel.Moderate,
                RequiresElevation = true,
                RequiresApproval = true,
                PreValidator = ctx => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 3. File or Path Not Found
        if (PathNotFoundRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.PathNotFound;
            diag.PlainEnglishSummary = "Target Not Found: The specified file or folder path does not exist.";
            diag.LikelyCause = "The path was misspelled, was previously deleted, or parent directories have not been created.";

            // Attempt to extract missing path from error or translation
            string? missingPath = ExtractMissingPath(rawError, translation);
            if (!string.IsNullOrEmpty(missingPath))
            {
                var dir = Path.GetDirectoryName(missingPath);
                if (!string.IsNullOrEmpty(dir) && dir != ".")
                {
                    diag.SuggestedFix = new SuggestedRemediation
                    {
                        Description = $"Create missing parent directory '{dir}' and retry.",
                        RemediationScript = $"New-Item -ItemType Directory -Path '{dir}' -Force | Out-Null; Write-Host \"Created directory: {dir}\"",
                        RetryScript = translation?.PowerShellScript,
                        Risk = RiskLevel.Moderate,
                        RequiresApproval = true,
                        PreValidator = _ => Task.FromResult(new RemediationValidationReport(!Directory.Exists(dir), "Parent directory does not exist."))
                    };
                }
            }
            return diag;
        }

        // 4. Command / Executable Missing
        if (CommandNotFoundRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.CommandNotFound;
            diag.PlainEnglishSummary = "Command Not Found: The program or cmdlet is not installed or not in your system PATH.";
            diag.LikelyCause = "The application is not installed on this machine, or its executable directory is missing from %PATH%.";

            var cmdMatch = Regex.Match(rawError, @"(?:term\s+'(?<cmd>[^']+)'|name\s+'(?<cmd>[^']+)')", RegexOptions.IgnoreCase);
            var cmdName = cmdMatch.Success ? cmdMatch.Groups["cmd"].Value : "the executable";

            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = $"Search for '{cmdName}' on Windows Package Manager (winget) or inspect system PATH.",
                RemediationScript = $"winget search '{cmdName}'",
                Risk = RiskLevel.Low,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 5. Process Not Found
        if (ProcessNotFoundRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.ProcessNotFound;
            diag.PlainEnglishSummary = "Process Not Found: No active process matches the given name or identifier.";
            diag.LikelyCause = "The process has already exited, was never started, or the name/PID was misspelled.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "List the top active processes to find the current process name or PID.",
                RemediationScript = "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 15 Name, Id, @{Name='RAM_MB';Expression={[math]::Round($_.WorkingSet64/1MB,1)}}",
                Risk = RiskLevel.Safe,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 6. Windows Service Not Found
        if (ServiceNotFoundRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.ServiceNotFound;
            diag.PlainEnglishSummary = "Windows Service Not Found: No service matches the specified name.";
            diag.LikelyCause = "The service registry name differs from its user-facing display name, or the service is not installed.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Search all installed Windows services to find the correct system name.",
                RemediationScript = "Get-Service | Select-Object -First 25 Name, DisplayName, Status",
                Risk = RiskLevel.Safe,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 7. Resource Locked / File In Use
        if (ResourceLockedRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.ResourceLocked;
            diag.PlainEnglishSummary = "File Locked: The target file is currently in use by another application.";
            diag.LikelyCause = "An active background process or editor has opened the file with an exclusive lock.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Inspect running processes holding open file handles or retry in a few seconds.",
                RemediationScript = translation?.PowerShellScript,
                Risk = RiskLevel.Moderate,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 8. Port In Use / Network Conflict
        if (PortInUseRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.PortInUse;
            diag.PlainEnglishSummary = "Port Conflict: Another process is already bound to this network port.";
            diag.LikelyCause = "A web server, Docker daemon, or local service is actively listening on this port.";

            var portMatch = Regex.Match(rawError, @"\b(?<port>\d{2,5})\b");
            var port = portMatch.Success ? portMatch.Groups["port"].Value : "3000";

            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = $"Find the process currently listening on port {port}.",
                RemediationScript = $"Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, OwningProcess, State",
                Risk = RiskLevel.Safe,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 9. Network / Host Unreachable
        if (NetworkFailureRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.NetworkFailure;
            diag.PlainEnglishSummary = "Network Failure: Could not reach the destination host or URL.";
            diag.LikelyCause = "Internet connection is down, DNS resolution failed, or remote host refused connection.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Clear local DNS cache and test Internet gateway connectivity.",
                RemediationScript = "Clear-DnsClientCache; Test-Connection 8.8.8.8 -Count 2",
                Risk = RiskLevel.Safe,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // 10. Disk Full
        if (DiskFullRegex.IsMatch(rawError))
        {
            diag.Category = ErrorCategory.DiskFull;
            diag.PlainEnglishSummary = "Disk Full: The destination storage volume has insufficient free space.";
            diag.LikelyCause = "The drive partition has run out of available bytes to write or extract this data.";
            diag.SuggestedFix = new SuggestedRemediation
            {
                Description = "Show disk partition free space and storage capacity.",
                RemediationScript = "Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' } | Select-Object DriveLetter, @{Name='FreeGB';Expression={[math]::Round($_.SizeRemaining/1GB,2)}}, @{Name='TotalGB';Expression={[math]::Round($_.Size/1GB,2)}}",
                Risk = RiskLevel.Safe,
                RequiresApproval = true,
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            };
            return diag;
        }

        // Default Unknown Fallback
        diag.Category = ErrorCategory.GeneralFailure;
        diag.PlainEnglishSummary = "Command execution failed with an unexpected error.";
        diag.LikelyCause = "The underlying tool returned an error or the parameters were rejected.";
        return diag;
    }

    private static string? ExtractMissingPath(string rawError, TranslationResult? translation)
    {
        var match = Regex.Match(rawError, @"Cannot\s+find\s+path\s+'(?<path>[^']+)'", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups["path"].Value;

        match = Regex.Match(rawError, @"Could\s+not\s+find\s+a\s+part\s+of\s+the\s+path\s+'(?<path>[^']+)'", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups["path"].Value;

        if (translation?.ExtractedEntities?.Paths.Count > 0)
        {
            return translation.ExtractedEntities.Paths.First();
        }

        return null;
    }
}
