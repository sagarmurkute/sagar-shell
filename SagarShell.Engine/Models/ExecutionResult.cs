using System;
using System.Collections.Generic;
using System.Linq;

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
    
    /// <summary>
    /// Preserved native .NET objects / PSObjects produced by the command pipeline.
    /// </summary>
    public List<object>? StructuredObjects { get; set; } = new();

    /// <summary>
    /// Indicates whether execution ran in-process via Runspace or via fallback process.
    /// </summary>
    public string ExecutionMode { get; set; } = "InProcessRunspace";

    public bool HasStructuredData => StructuredObjects != null && StructuredObjects.Count > 0;

    public IEnumerable<T> GetObjects<T>()
    {
        if (StructuredObjects == null) yield break;

        foreach (var item in StructuredObjects)
        {
            if (item is T typed)
            {
                yield return typed;
            }
            else if (item is System.Management.Automation.PSObject psObj && psObj.BaseObject is T baseTyped)
            {
                yield return baseTyped;
            }
        }
    }

    public static ExecutionResult CreateSuccess(string stdout, TimeSpan duration, string? newCwd = null, List<object>? objects = null, string executionMode = "InProcessRunspace")
    {
        return new ExecutionResult
        {
            Success = true,
            ExitCode = 0,
            StandardOutput = stdout,
            Duration = duration,
            TargetDirectoryAfterExecution = newCwd,
            StructuredObjects = objects ?? new List<object>(),
            ExecutionMode = executionMode
        };
    }

    public static ExecutionResult CreateFailure(string stderr, int exitCode, TimeSpan duration, string? stdout = null, string executionMode = "InProcessRunspace")
    {
        return new ExecutionResult
        {
            Success = false,
            ExitCode = exitCode,
            StandardError = stderr,
            StandardOutput = stdout ?? string.Empty,
            Duration = duration,
            ExecutionMode = executionMode
        };
    }
}
