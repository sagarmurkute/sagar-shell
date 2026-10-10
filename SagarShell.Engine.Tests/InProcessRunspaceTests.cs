using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class InProcessRunspaceTests
{
    private readonly NaturalLanguageEngine _engine = new();

    [Fact]
    public async Task Runspace_ShouldPersistVariablesAcrossCommands()
    {
        using var executor = new InProcessRunspaceExecutor();
        var context = new ShellContext(Directory.GetCurrentDirectory());

        // Step 1: Set variable $sessionKey = 12345
        var setRes = await executor.ExecuteScriptAsync("$sessionKey = 12345; Write-Output 'Variable Set'", context);
        Assert.True(setRes.Success, $"Step 1 failed with error: {setRes.StandardError} | Stdout: {setRes.StandardOutput}");
        Assert.Contains("Variable Set", setRes.StandardOutput);

        // Step 2: Compute $sessionKey * 2
        var calcRes = await executor.ExecuteScriptAsync("$computed = $sessionKey * 2; Write-Output \"Result: $computed\"", context);
        Assert.True(calcRes.Success, $"Step 2 failed with error: {calcRes.StandardError} | Stdout: {calcRes.StandardOutput}");
        Assert.Contains("Result: 24690", calcRes.StandardOutput);

        // Verify variable directly via executor API
        var varVal = executor.GetVariable("sessionKey");
        Assert.Equal(12345, Convert.ToInt32(varVal));
    }

    [Fact]
    public async Task Runspace_ShouldPersistCustomFunctionsAcrossCommands()
    {
        using var executor = new InProcessRunspaceExecutor();
        var context = new ShellContext(Directory.GetCurrentDirectory());

        // Step 1: Define a custom function
        var defRes = await executor.ExecuteScriptAsync("function SagarMultiply($a, $b) { return $a * $b }", context);
        Assert.True(defRes.Success, $"Define function failed: {defRes.StandardError}");

        // Step 2: Call the custom function
        var callRes = await executor.ExecuteScriptAsync("SagarMultiply 7 8", context);
        Assert.True(callRes.Success, $"Call function failed: {callRes.StandardError}");
        Assert.Contains("56", callRes.StandardOutput);
    }

    [Fact]
    public async Task Runspace_ShouldPreserveNativeStructuredObjects()
    {
        using var executor = new InProcessRunspaceExecutor();
        var context = new ShellContext(Directory.GetCurrentDirectory());

        var result = await executor.ExecuteScriptAsync("Get-Process | Select-Object -First 3 Name, Id", context);

        Assert.True(result.Success, $"Get-Process failed: {result.StandardError}");
        Assert.True(result.HasStructuredData);
        Assert.NotNull(result.StructuredObjects);
        Assert.Equal(3, result.StructuredObjects.Count);

        // Verify properties on first PSObject
        var firstObj = result.StructuredObjects[0] as PSObject;
        Assert.NotNull(firstObj);
        Assert.NotNull(firstObj.Properties["Name"]);
        Assert.NotNull(firstObj.Properties["Id"]);
    }

    [Fact]
    public async Task Runspace_ShouldSynchronizeWorkingDirectoryState()
    {
        using var executor = new InProcessRunspaceExecutor();
        var initialDir = Directory.GetCurrentDirectory();
        var context = new ShellContext(initialDir);

        // Step 1: Navigate to parent directory
        var navRes = await executor.ExecuteScriptAsync("Set-Location ..", context);
        Assert.True(navRes.Success, $"Set-Location .. failed: {navRes.StandardError}");

        var parentDir = Directory.GetParent(initialDir)?.FullName ?? initialDir;
        Assert.Equal(Path.GetFullPath(parentDir), Path.GetFullPath(context.CurrentDirectory));

        // Step 2: Navigate back to initial directory
        var returnRes = await executor.ExecuteScriptAsync($"Set-Location '{initialDir.Replace("'", "''")}'", context);
        Assert.True(returnRes.Success, $"Set-Location back failed: {returnRes.StandardError}");
        Assert.Equal(Path.GetFullPath(initialDir), Path.GetFullPath(context.CurrentDirectory));
    }

    [Fact]
    public async Task ShellRuntimeManager_ShouldUseRunspaceByDefaultAndSupportProcessFallback()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());

        // Test 1: Default runtime (Runspace)
        using var runtimeDefault = new ShellRuntimeManager(Directory.GetCurrentDirectory());
        Assert.True(runtimeDefault.IsRunspaceActive);

        var runspaceRes = await runtimeDefault.ExecuteScriptAsync("Write-Output 'Hello from Runspace'", context);
        Assert.True(runspaceRes.Success, $"Runspace exec failed: {runspaceRes.StandardError}");
        Assert.Contains("Hello from Runspace", runspaceRes.StandardOutput);
        Assert.Equal("InProcessRunspace", runspaceRes.ExecutionMode);

        // Test 2: Explicit Fallback mode
        using var runtimeFallback = new ShellRuntimeManager(Directory.GetCurrentDirectory(), preferProcessFallback: true);
        Assert.False(runtimeFallback.IsRunspaceActive);

        var processRes = await runtimeFallback.ExecuteScriptAsync("Write-Output 'Hello from Fallback Process'", context);
        Assert.True(processRes.Success, $"Process exec failed: {processRes.StandardError}");
        Assert.Contains("Hello from Fallback Process", processRes.StandardOutput);
    }
}
