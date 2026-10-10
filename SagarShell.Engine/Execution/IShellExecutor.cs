using System;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Execution;

/// <summary>
/// Execution dispatcher contract for running translated commands on Windows.
/// </summary>
public interface IShellExecutor
{
    event Action<string>? OutputReceived;
    event Action<string>? ErrorReceived;

    Task<ExecutionResult> ExecuteAsync(TranslationResult translation, ShellContext context, CancellationToken cancellationToken = default);
    Task<ExecutionResult> ExecuteScriptAsync(string powerShellScript, ShellContext context, CancellationToken cancellationToken = default);
}
