using System;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// Orchestrates safe, validated error recovery with mandatory user approval gates.
/// Prevents blind automatic execution of fixes while enabling reliable guided retries.
/// </summary>
public class RecoveryManager
{
    /// <summary>
    /// Validates the remediation action before presenting it to the user for approval.
    /// </summary>
    public async Task<RemediationValidationReport> ValidateRemediationAsync(SuggestedRemediation remediation, ShellContext context)
    {
        if (remediation == null)
            return new RemediationValidationReport(false, "No remediation proposed.");

        if (string.IsNullOrWhiteSpace(remediation.RemediationScript) && string.IsNullOrWhiteSpace(remediation.RetryScript))
            return new RemediationValidationReport(false, "Remediation does not contain any executable action.");

        if (remediation.PreValidator != null)
        {
            var report = await remediation.PreValidator(context);
            if (!report.IsValid)
            {
                return report;
            }
        }

        return new RemediationValidationReport(true);
    }

    /// <summary>
    /// Executes the validated remediation workflow upon explicit user approval.
    /// </summary>
    public async Task<RecoveryResult> ApplyRecoveryAsync(
        ErrorDiagnosis diagnosis,
        IShellExecutor executor,
        ShellContext context,
        Func<SuggestedRemediation, Task<bool>> approvalPrompt,
        CancellationToken cancellationToken = default)
    {
        var result = new RecoveryResult();

        if (diagnosis.SuggestedFix == null)
        {
            result.SummaryMessage = "No remediation suggested for this error.";
            return result;
        }

        var fix = diagnosis.SuggestedFix;

        // 1. Pre-validation Gate
        var validation = await ValidateRemediationAsync(fix, context);
        result.ValidationPassed = validation.IsValid;
        result.ValidationFailureReason = validation.Reason;

        if (!validation.IsValid)
        {
            result.SummaryMessage = $"Remediation pre-validation failed: {validation.Reason}";
            return result;
        }

        // 2. Mandatory Approval Gate (No blind automatic execution)
        bool approved = await approvalPrompt(fix);
        result.ApprovalGranted = approved;

        if (!approved)
        {
            result.SummaryMessage = "Remediation cancelled by user.";
            return result;
        }

        // 3. Execute Remediation Script
        if (!string.IsNullOrWhiteSpace(fix.RemediationScript))
        {
            result.RemediationAttempted = true;
            var remTranslation = TranslationResult.CreateSuccess(
                "Remediation: " + fix.Description,
                fix.RemediationScript,
                fix.Description,
                fix.Risk,
                IntentCategory.SystemInformation
            );

            var remExec = await executor.ExecuteAsync(remTranslation, context, cancellationToken);
            result.RemediationOutput = remExec;
            result.RemediationSucceeded = remExec.Success;

            if (!remExec.Success)
            {
                result.SummaryMessage = $"Remediation failed: {remExec.StandardError}";
                return result;
            }
        }
        else
        {
            result.RemediationSucceeded = true;
        }

        // 4. Retry Original Operation if applicable
        if (fix.CanAutoRetryAfterFix && !string.IsNullOrWhiteSpace(fix.RetryScript))
        {
            result.RetryAttempted = true;
            var retryTranslation = TranslationResult.CreateSuccess(
                "Retry after remediation",
                fix.RetryScript,
                "Retrying original operation after applying fix.",
                RiskLevel.Moderate,
                IntentCategory.GeneralQuery
            );

            var retryExec = await executor.ExecuteAsync(retryTranslation, context, cancellationToken);
            result.RetryOutput = retryExec;
            result.RetrySucceeded = retryExec.Success;

            result.SummaryMessage = retryExec.Success
                ? "Remediation applied and operation succeeded on retry!"
                : $"Remediation applied, but retry failed: {retryExec.StandardError}";
        }
        else
        {
            result.SummaryMessage = "Remediation applied successfully.";
        }

        return result;
    }
}
