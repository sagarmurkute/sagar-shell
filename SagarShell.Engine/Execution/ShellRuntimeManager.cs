using System;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Execution;

/// <summary>
/// Manages shell execution runtimes. Uses the in-process PowerShell Runspace by default
/// and automatically falls back to the external process executor if needed.
/// </summary>
public class ShellRuntimeManager : IShellExecutor, IDisposable, IAsyncDisposable
{
    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;

    private readonly InProcessRunspaceExecutor? _runspaceExecutor;
    private readonly PowerShellExecutor _processExecutor;
    private bool _preferProcessFallback;

    public bool IsRunspaceActive => _runspaceExecutor != null && _runspaceExecutor.IsOpen && !_preferProcessFallback;
    public IShellExecutor ActiveExecutor => IsRunspaceActive ? _runspaceExecutor! : _processExecutor;

    public ShellRuntimeManager(string? initialDirectory = null, bool preferProcessFallback = false)
    {
        _preferProcessFallback = preferProcessFallback;
        _processExecutor = new PowerShellExecutor();
        _processExecutor.OutputReceived += msg => OutputReceived?.Invoke(msg);
        _processExecutor.ErrorReceived += err => ErrorReceived?.Invoke(err);

        try
        {
            _runspaceExecutor = new InProcessRunspaceExecutor(initialDirectory);
            _runspaceExecutor.OutputReceived += msg => OutputReceived?.Invoke(msg);
            _runspaceExecutor.ErrorReceived += err => ErrorReceived?.Invoke(err);
        }
        catch
        {
            // If in-process runspace fails to initialize on target OS/environment, gracefully fallback to process executor
            _runspaceExecutor = null;
            _preferProcessFallback = true;
        }
    }

    public void EnableProcessFallback(bool enable)
    {
        _preferProcessFallback = enable;
    }

    public async Task<ExecutionResult> ExecuteAsync(TranslationResult translation, ShellContext context, CancellationToken cancellationToken = default)
    {
        if (IsRunspaceActive)
        {
            try
            {
                return await _runspaceExecutor!.ExecuteAsync(translation, context, cancellationToken);
            }
            catch
            {
                // Fallback to process executor
                return await _processExecutor.ExecuteAsync(translation, context, cancellationToken);
            }
        }

        return await _processExecutor.ExecuteAsync(translation, context, cancellationToken);
    }

    public async Task<ExecutionResult> ExecuteScriptAsync(string powerShellScript, ShellContext context, CancellationToken cancellationToken = default)
    {
        if (IsRunspaceActive)
        {
            try
            {
                return await _runspaceExecutor!.ExecuteScriptAsync(powerShellScript, context, cancellationToken);
            }
            catch
            {
                // Fallback to process executor
                return await _processExecutor.ExecuteScriptAsync(powerShellScript, context, cancellationToken);
            }
        }

        return await _processExecutor.ExecuteScriptAsync(powerShellScript, context, cancellationToken);
    }

    public void Dispose()
    {
        _runspaceExecutor?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_runspaceExecutor != null)
        {
            await _runspaceExecutor.DisposeAsync();
        }
    }
}
