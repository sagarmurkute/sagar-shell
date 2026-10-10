using System;

namespace SagarShell.Engine.Diagnosis;

/// <summary>
/// Categories of Windows / PowerShell runtime and environmental execution failures.
/// </summary>
public enum ErrorCategory
{
    None,
    AccessDenied,
    ElevationRequired,
    FileNotFound,
    PathNotFound,
    CommandNotFound,
    ProcessNotFound,
    ServiceNotFound,
    ResourceLocked,
    PortInUse,
    NetworkFailure,
    HostUnreachable,
    InvalidParameter,
    SyntaxError,
    DiskFull,
    ExecutionTimeout,
    ScriptExecutionDisabled,
    GeneralFailure,
    Unknown
}
