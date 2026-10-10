using System;
using System.Collections.Generic;

namespace SagarShell.Engine.Models;

/// <summary>
/// Output and status from executing a translated command.
/// </summary>
public class ExecutionResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string? TargetDirectoryAfterExecution { get; set; }
    public List<object>? StructuredObjects { get; set; }

    public static ExecutionResult CreateSuccess(string stdout, TimeSpan duration, string? newCwd = null)
    {
        return new ExecutionResult
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = stdout,
            Duration = duration,
            TargetDirectoryAfterExecution = newCwd
        };
    }

    public static ExecutionResult CreateFailure(string stderr, int exitCode, TimeSpan duration, string? stdout = null)
    {
        return new ExecutionResult
        {
            Success = false,
            ExitCode = exitCode,
            StandardError = stderr,
            StandardOutput = stdout ?? string.Empty,
            Duration = duration
        };
    }
}
