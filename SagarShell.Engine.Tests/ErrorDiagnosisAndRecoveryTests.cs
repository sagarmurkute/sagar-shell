using System;
using System.IO;
using System.Threading.Tasks;
using SagarShell.Engine.Diagnosis;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class ErrorDiagnosisAndRecoveryTests
{
    [Fact]
    public void Diagnose_AccessDenied_IdentifiesCategoryAndElevationFix()
    {
        var rawError = "System.UnauthorizedAccessException: Access to the path 'C:\\Windows\\System32\\drivers\\etc\\hosts' is denied.";
        var translation = TranslationResult.CreateSuccess("edit hosts", "Set-Content C:\\Windows\\System32\\drivers\\etc\\hosts '127.0.0.1 test'", "Edits hosts file.");
        var context = new ShellContext { IsElevated = false };

        var diag = ErrorDiagnoser.Diagnose(rawError, 1, translation, context);

        Assert.Equal(ErrorCategory.AccessDenied, diag.Category);
        Assert.Contains("Access Denied", diag.PlainEnglishSummary);
        Assert.Contains("Administrator", diag.LikelyCause);
        Assert.NotNull(diag.SuggestedFix);
        Assert.True(diag.SuggestedFix.RequiresElevation);
        Assert.Contains("Start-Process", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_PathNotFound_IdentifiesCategoryAndSuggestsDirectoryCreation()
    {
        var rawError = "Set-Content : Cannot find path 'C:\\Temp\\NonExistentFolder\\file.txt' because it does not exist.";
        var translation = TranslationResult.CreateSuccess(
            "create file C:\\Temp\\NonExistentFolder\\file.txt",
            "Set-Content -Path 'C:\\Temp\\NonExistentFolder\\file.txt' -Value 'test'",
            "Creates file.");
        translation.ExtractedEntities!.Paths.Add("C:\\Temp\\NonExistentFolder\\file.txt");

        var diag = ErrorDiagnoser.Diagnose(rawError, 1, translation);

        Assert.Equal(ErrorCategory.PathNotFound, diag.Category);
        Assert.Contains("Not Found", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("New-Item -ItemType Directory", diag.SuggestedFix.RemediationScript);
        Assert.Equal(translation.PowerShellScript, diag.SuggestedFix.RetryScript);
    }

    [Fact]
    public void Diagnose_CommandNotFound_IdentifiesCategoryAndSuggestsWingetSearch()
    {
        var rawError = "The term 'kubectl' is not recognized as the name of a cmdlet, function, script file, or operable program.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.CommandNotFound, diag.Category);
        Assert.Contains("Command Not Found", diag.PlainEnglishSummary);
        Assert.Contains("PATH", diag.LikelyCause);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("winget search 'kubectl'", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_ProcessNotFound_IdentifiesCategoryAndSuggestsActiveProcessList()
    {
        var rawError = "Stop-Process : Cannot find a process with the process identifier 99999.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.ProcessNotFound, diag.Category);
        Assert.Contains("Process Not Found", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Get-Process", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_ServiceNotFound_IdentifiesCategoryAndSuggestsServiceSearch()
    {
        var rawError = "Get-Service : Cannot find any service with service name 'MyFakeService'.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.ServiceNotFound, diag.Category);
        Assert.Contains("Service Not Found", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Get-Service", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_ResourceLocked_IdentifiesFileLock()
    {
        var rawError = "System.IO.IOException: The process cannot access the file 'C:\\App\\data.db' because it is being used by another process.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.ResourceLocked, diag.Category);
        Assert.Contains("Locked", diag.PlainEnglishSummary);
        Assert.Contains("exclusive lock", diag.LikelyCause);
    }

    [Fact]
    public void Diagnose_PortInUse_IdentifiesPortConflict()
    {
        var rawError = "System.Net.Sockets.SocketException: Only one usage of each socket address (protocol/network address/port) is normally permitted on port 8080.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.PortInUse, diag.Category);
        Assert.Contains("Port Conflict", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Get-NetTCPConnection -LocalPort 8080", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_NetworkFailure_IdentifiesNetworkError()
    {
        var rawError = "System.Net.WebException: The remote name could not be resolved: 'api.example.com'";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.NetworkFailure, diag.Category);
        Assert.Contains("Network Failure", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Clear-DnsClientCache", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_DiskFull_IdentifiesStorageExhaustion()
    {
        var rawError = "System.IO.IOException: There is not enough space on the disk.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.DiskFull, diag.Category);
        Assert.Contains("Disk Full", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Get-Volume", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public void Diagnose_ScriptExecutionDisabled_SuggestsRemoteSignedPolicyFix()
    {
        var rawError = "File C:\\script.ps1 cannot be loaded because running scripts is disabled on this system.";
        var diag = ErrorDiagnoser.Diagnose(rawError, 1);

        Assert.Equal(ErrorCategory.ScriptExecutionDisabled, diag.Category);
        Assert.Contains("Script Execution Blocked", diag.PlainEnglishSummary);
        Assert.NotNull(diag.SuggestedFix);
        Assert.Contains("Set-ExecutionPolicy", diag.SuggestedFix.RemediationScript);
    }

    [Fact]
    public async Task RecoveryManager_UserApprovalDenied_AbortsWithoutExecuting()
    {
        var manager = new RecoveryManager();
        var context = new ShellContext();
        using var executor = new ShellRuntimeManager();

        var diagnosis = new ErrorDiagnosis
        {
            Category = ErrorCategory.AccessDenied,
            PlainEnglishSummary = "Test error",
            SuggestedFix = new SuggestedRemediation
            {
                Description = "Test fix",
                RemediationScript = "Write-Host 'Should Not Run'",
                RequiresApproval = true
            }
        };

        // User denies approval
        var result = await manager.ApplyRecoveryAsync(
            diagnosis,
            executor,
            context,
            approvalPrompt: _ => Task.FromResult(false));

        Assert.False(result.ApprovalGranted);
        Assert.False(result.RemediationAttempted);
        Assert.Contains("cancelled by user", result.SummaryMessage);
    }

    [Fact]
    public async Task RecoveryManager_PreValidationFails_AbortsBeforeApproval()
    {
        var manager = new RecoveryManager();
        var context = new ShellContext();
        using var executor = new ShellRuntimeManager();

        var diagnosis = new ErrorDiagnosis
        {
            Category = ErrorCategory.PathNotFound,
            PlainEnglishSummary = "Path error",
            SuggestedFix = new SuggestedRemediation
            {
                Description = "Invalid fix",
                RemediationScript = "New-Item ...",
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(false, "Pre-condition unmet."))
            }
        };

        var result = await manager.ApplyRecoveryAsync(
            diagnosis,
            executor,
            context,
            approvalPrompt: _ => Task.FromResult(true));

        Assert.False(result.ValidationPassed);
        Assert.Equal("Pre-condition unmet.", result.ValidationFailureReason);
        Assert.False(result.RemediationAttempted);
    }

    [Fact]
    public async Task RecoveryManager_UserApproves_ExecutesRemediationAndRetriesSuccessfully()
    {
        var manager = new RecoveryManager();
        var context = new ShellContext();
        using var executor = new ShellRuntimeManager();

        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestRecoveryFolder");
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);

        var diagnosis = new ErrorDiagnosis
        {
            Category = ErrorCategory.PathNotFound,
            PlainEnglishSummary = "Directory missing",
            SuggestedFix = new SuggestedRemediation
            {
                Description = "Create missing folder TestRecoveryFolder",
                RemediationScript = $"New-Item -ItemType Directory -Path '{testDir}' -Force",
                RetryScript = $"Set-Content -Path '{Path.Combine(testDir, "output.txt")}' -Value 'RecoverySuccess'",
                PreValidator = _ => Task.FromResult(new RemediationValidationReport(true))
            }
        };

        var result = await manager.ApplyRecoveryAsync(
            diagnosis,
            executor,
            context,
            approvalPrompt: _ => Task.FromResult(true));

        Assert.True(result.ValidationPassed);
        Assert.True(result.ApprovalGranted);
        Assert.True(result.RemediationAttempted);
        Assert.True(result.RemediationSucceeded);
        Assert.True(result.RetryAttempted);
        Assert.True(result.RetrySucceeded);
        Assert.Contains("succeeded on retry", result.SummaryMessage);

        var createdFile = Path.Combine(testDir, "output.txt");
        Assert.True(File.Exists(createdFile));
        Assert.Equal("RecoverySuccess", File.ReadAllText(createdFile).Trim());

        // Cleanup
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
    }
}
