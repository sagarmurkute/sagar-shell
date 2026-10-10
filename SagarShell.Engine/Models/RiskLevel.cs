namespace SagarShell.Engine.Models;

/// <summary>
/// Defines the safety and impact risk level of an operation.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Read-only operations, queries, status checks (e.g. Get-ChildItem, Get-Process, ipconfig).
    /// Safe to execute automatically.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Harmless navigation or UI state operations (e.g. cd, clear-host).
    /// </summary>
    Low = 1,

    /// <summary>
    /// Non-destructive modifications (e.g. creating a new folder, copying a file, starting an app).
    /// </summary>
    Moderate = 2,

    /// <summary>
    /// Modifying or terminating resources (e.g. killing a process, stopping a service, moving/renaming files).
    /// </summary>
    High = 3,

    /// <summary>
    /// Destructive or system-altering actions (e.g. recursive deletion, disk format, clearing recycle bin, registry changes, system shutdown).
    /// Requires explicit user confirmation / dry-run preview.
    /// </summary>
    Critical = 4
}
