using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Execution;

/// <summary>
/// Executes PowerShell commands and scripts with real-time streaming, timeout support, and directory tracking.
/// </summary>
public class PowerShellExecutor : IShellExecutor
{
    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;

    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(60);

    public async Task<ExecutionResult> ExecuteAsync(TranslationResult translation, ShellContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(translation.PowerShellScript))
        {
            return ExecutionResult.CreateFailure("No executable PowerShell script provided.", 1, TimeSpan.Zero);
        }

        // Check if there is a fast direct native action
        if (translation.DirectNativeAction != null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var res = translation.DirectNativeAction(context);
                context.AddHistory(translation.OriginalInput, translation, res);
                return res;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var fail = ExecutionResult.CreateFailure(ex.Message, 1, sw.Elapsed);
                fail.Diagnosis = SagarShell.Engine.Diagnosis.ErrorDiagnoser.Diagnose(fail, translation, context);
                context.AddHistory(translation.OriginalInput, translation, fail);
                return fail;
            }
        }

        var execResult = await ExecuteScriptAsync(translation.PowerShellScript, context, cancellationToken);
        if (!execResult.Success)
        {
            execResult.Diagnosis = SagarShell.Engine.Diagnosis.ErrorDiagnoser.Diagnose(execResult, translation, context);
        }
        context.AddHistory(translation.OriginalInput, translation, execResult);
        return execResult;
    }

    public async Task<ExecutionResult> ExecuteScriptAsync(string powerShellScript, ShellContext context, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // Wrap script to capture working directory if Set-Location is executed
        var wrappedScript = $@"
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
{powerShellScript}
Write-Output ""__SAGAR_SHELL_PWD_MARKER__:$((Get-Location).Path)""
";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{wrappedScript.Replace("\"", "`\"")}\"",
            WorkingDirectory = Directory.Exists(context.CurrentDirectory) ? context.CurrentDirectory : Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_defaultTimeout);

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    if (e.Data.StartsWith("__SAGAR_SHELL_PWD_MARKER__:"))
                    {
                        var newPath = e.Data["__SAGAR_SHELL_PWD_MARKER__:".Length..].Trim();
                        if (Directory.Exists(newPath))
                        {
                            context.CurrentDirectory = newPath;
                        }
                    }
                    else
                    {
                        stdout.AppendLine(e.Data);
                        OutputReceived?.Invoke(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    stderr.AppendLine(e.Data);
                    ErrorReceived?.Invoke(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cts.Token);
            sw.Stop();

            var outStr = stdout.ToString().TrimEnd();
            var errStr = stderr.ToString().TrimEnd();

            if (process.ExitCode == 0)
            {
                return ExecutionResult.CreateSuccess(outStr, sw.Elapsed, context.CurrentDirectory);
            }
            else
            {
                var fail = ExecutionResult.CreateFailure(errStr.Length > 0 ? errStr : outStr, process.ExitCode, sw.Elapsed, outStr);
                fail.Diagnosis = SagarShell.Engine.Diagnosis.ErrorDiagnoser.Diagnose(fail, null, context);
                return fail;
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var fail = ExecutionResult.CreateFailure("Operation timed out or was cancelled by user.", -1, sw.Elapsed);
            fail.Diagnosis = SagarShell.Engine.Diagnosis.ErrorDiagnoser.Diagnose(fail, null, context);
            return fail;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var fail = ExecutionResult.CreateFailure($"Process execution error: {ex.Message}", -1, sw.Elapsed);
            fail.Diagnosis = SagarShell.Engine.Diagnosis.ErrorDiagnoser.Diagnose(fail, null, context);
            return fail;
        }
    }
}
