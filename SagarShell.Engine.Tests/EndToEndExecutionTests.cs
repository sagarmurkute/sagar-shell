using System;
using System.IO;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class EndToEndExecutionTests
{
    private readonly NaturalLanguageEngine _engine = new();
    private readonly PowerShellExecutor _executor = new();

    [Fact]
    public async Task EndToEnd_ListFiles_TranslatesAndExecutesSuccessfully()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var request = new TranslationRequest("list files", context);

        var translation = await _engine.TranslateAsync(request);
        Assert.NotNull(translation.PowerShellScript);
        Assert.Equal(RiskLevel.Safe, translation.Risk);

        var execution = await _executor.ExecuteAsync(translation, context);

        Assert.True(execution.Success);
        Assert.NotEmpty(execution.StandardOutput);
        Assert.Single(context.History);
    }

    [Fact]
    public async Task EndToEnd_SystemUptime_TranslatesAndExecutesSuccessfully()
    {
        var context = new ShellContext();
        var request = new TranslationRequest("system uptime", context);

        var translation = await _engine.TranslateAsync(request);
        var execution = await _executor.ExecuteAsync(translation, context);

        Assert.True(execution.Success);
        Assert.Contains("Days", execution.StandardOutput);
    }

    [Fact]
    public async Task EndToEnd_DiskSpace_TranslatesAndExecutesSuccessfully()
    {
        var context = new ShellContext();
        var request = new TranslationRequest("show disk space", context);

        var translation = await _engine.TranslateAsync(request);
        var execution = await _executor.ExecuteAsync(translation, context);

        Assert.True(execution.Success);
        Assert.Contains("SizeGB", execution.StandardOutput);
    }
}
