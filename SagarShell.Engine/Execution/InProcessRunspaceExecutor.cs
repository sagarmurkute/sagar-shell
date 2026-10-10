using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Execution;

/// <summary>
/// High-performance in-process PowerShell Runspace host.
/// Preserves session variables ($x = 10), functions, modules, and typed .NET objects across commands with zero process spawn latency.
/// </summary>
public class InProcessRunspaceExecutor : IShellExecutor, IDisposable, IAsyncDisposable
{
    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;

    private readonly Runspace _runspace;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isDisposed;

    public bool IsOpen => _runspace != null && _runspace.RunspaceStateInfo.State == RunspaceState.Opened;

    public InProcessRunspaceExecutor(string? initialDirectory = null)
    {
        var sessionState = InitialSessionState.CreateDefault();
        sessionState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

        _runspace = RunspaceFactory.CreateRunspace(sessionState);
        _runspace.ApartmentState = ApartmentState.STA;
        _runspace.ThreadOptions = PSThreadOptions.ReuseThread;
        _runspace.Open();

        // Set initial location
        var path = !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? Path.GetFullPath(initialDirectory)
            : Directory.GetCurrentDirectory();

        SetLocationInternal(path);
    }

    public async Task<ExecutionResult> ExecuteAsync(TranslationResult translation, ShellContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(translation.PowerShellScript))
        {
            return ExecutionResult.CreateFailure("No executable PowerShell script provided.", 1, TimeSpan.Zero, executionMode: "InProcessRunspace");
        }

        // Direct Native C# Action fast-path if present
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
                var fail = ExecutionResult.CreateFailure(ex.Message, 1, sw.Elapsed, executionMode: "DirectNative");
                context.AddHistory(translation.OriginalInput, translation, fail);
                return fail;
            }
        }

        var execResult = await ExecuteScriptAsync(translation.PowerShellScript, context, cancellationToken);
        context.AddHistory(translation.OriginalInput, translation, execResult);
        return execResult;
    }

    public async Task<ExecutionResult> ExecuteScriptAsync(string powerShellScript, ShellContext context, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sw = Stopwatch.StartNew();
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var structuredObjects = new List<object>();

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Sync context working directory to runspace if it was changed externally
            SyncLocationWithContext(context);

            using var ps = PowerShell.Create();
            ps.Runspace = _runspace;

            // Hook output streams
            ps.Streams.Information.DataAdded += (s, e) =>
            {
                var item = ps.Streams.Information[e.Index]?.MessageData?.ToString();
                if (!string.IsNullOrEmpty(item))
                {
                    stdoutBuilder.AppendLine(item);
                    OutputReceived?.Invoke(item);
                }
            };

            ps.Streams.Warning.DataAdded += (s, e) =>
            {
                var warning = ps.Streams.Warning[e.Index]?.Message;
                if (!string.IsNullOrEmpty(warning))
                {
                    stdoutBuilder.AppendLine($"WARNING: {warning}");
                    OutputReceived?.Invoke($"WARNING: {warning}");
                }
            };

            ps.Streams.Error.DataAdded += (s, e) =>
            {
                var err = ps.Streams.Error[e.Index]?.ToString();
                if (!string.IsNullOrEmpty(err))
                {
                    stderrBuilder.AppendLine(err);
                    ErrorReceived?.Invoke(err);
                }
            };

            ps.AddScript(powerShellScript);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var reg = cts.Token.Register(() =>
            {
                try { ps.Stop(); } catch { }
            });

            // Execute script on Runspace asynchronously
            Collection<PSObject> results = await Task.Run(() => ps.Invoke(), cts.Token);

            if (results != null && results.Count > 0)
            {
                foreach (var item in results)
                {
                    if (item != null)
                    {
                        structuredObjects.Add(item);
                        var str = item.ToString();
                        if (!string.IsNullOrWhiteSpace(str))
                        {
                            stdoutBuilder.AppendLine(str);
                            OutputReceived?.Invoke(str);
                        }
                    }
                }
            }

            sw.Stop();

            // Synchronize location back to context
            SyncLocationBackToContext(context);

            var stdout = stdoutBuilder.ToString().TrimEnd();
            var stderr = stderrBuilder.ToString().TrimEnd();

            if (!ps.HadErrors)
            {
                return ExecutionResult.CreateSuccess(stdout, sw.Elapsed, context.CurrentDirectory, structuredObjects, "InProcessRunspace");
            }
            else
            {
                if (stderr.Length == 0 && ps.Streams.Error.Count > 0)
                {
                    stderr = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
                }

                return ExecutionResult.CreateFailure(
                    string.IsNullOrEmpty(stderr) ? "Error occurred during script execution." : stderr,
                    1,
                    sw.Elapsed,
                    stdout,
                    "InProcessRunspace"
                );
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ExecutionResult.CreateFailure("Operation timed out or was cancelled by user.", -1, sw.Elapsed, executionMode: "InProcessRunspace");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.CreateFailure($"Runspace execution error: {ex.Message}", -1, sw.Elapsed, executionMode: "InProcessRunspace");
        }
        finally
        {
            _lock.Release();
        }
    }

    private void SyncLocationWithContext(ShellContext context)
    {
        try
        {
            var target = context.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
            {
                var currentLocation = _runspace.SessionStateProxy.Path.CurrentLocation.Path;
                if (!string.Equals(currentLocation, target, StringComparison.OrdinalIgnoreCase))
                {
                    SetLocationInternal(target);
                }
            }
        }
        catch
        {
            // Ignore path sync failure
        }
    }

    private void SyncLocationBackToContext(ShellContext context)
    {
        try
        {
            var currentLocation = _runspace.SessionStateProxy.Path.CurrentLocation.Path;
            if (!string.IsNullOrWhiteSpace(currentLocation) && Directory.Exists(currentLocation))
            {
                context.CurrentDirectory = currentLocation;
            }
        }
        catch
        {
            // Ignore location query failure
        }
    }

    private void SetLocationInternal(string path)
    {
        try
        {
            _runspace.SessionStateProxy.Path.SetLocation(path);
        }
        catch
        {
            // Ignore initial set location error
        }
    }

    public object? GetVariable(string variableName)
    {
        ThrowIfDisposed();
        return _runspace.SessionStateProxy.PSVariable.GetValue(variableName);
    }

    public void SetVariable(string variableName, object? value)
    {
        ThrowIfDisposed();
        _runspace.SessionStateProxy.PSVariable.Set(variableName, value);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(InProcessRunspaceExecutor));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _runspace.Close();
            _runspace.Dispose();
            _lock.Dispose();
        }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}
