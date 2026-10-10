using System;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Intelligence.Conversation;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class ConversationalIntelligenceTests
{
    private readonly NaturalLanguageEngine _engine;

    public ConversationalIntelligenceTests()
    {
        _engine = new NaturalLanguageEngine();
    }

    [Fact]
    public async Task MultiTurn_LaptopSlow_Diagnose_And_CloseHighestCpuProcess()
    {
        var context = new ShellContext();

        // Turn 1: Problem
        var req1 = new TranslationRequest("My laptop feels slow.", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.True(res1.IsConversationalResponse);
        Assert.True(res1.IsPendingProposal);
        Assert.NotNull(res1.PendingProposal);
        Assert.Contains("check running processes and resource usage", res1.ConversationalMessage);
        Assert.Equal("Diagnosing system slowdown", context.ActiveGoal);
        Assert.NotNull(context.ActiveProposal);

        context.RecordTurn(req1.NaturalLanguageInput, res1, null);

        // Turn 2: Affirm
        var req2 = new TranslationRequest("Yes.", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveProposal);
        Assert.False(res2.IsPendingProposal);
        Assert.Contains("Get-Process", res2.PowerShellScript);
        Assert.Contains("Sort-Object CPU -Descending", res2.PowerShellScript);

        // Simulate structured process results
        var psoChrome = new PSObject();
        psoChrome.Properties.Add(new PSNoteProperty("Id", 14200));
        psoChrome.Properties.Add(new PSNoteProperty("ProcessName", "chrome"));
        psoChrome.Properties.Add(new PSNoteProperty("CPU", 48.5));
        psoChrome.Properties.Add(new PSNoteProperty("RAM_MB", 1250.0));

        var psoCode = new PSObject();
        psoCode.Properties.Add(new PSNoteProperty("Id", 8320));
        psoCode.Properties.Add(new PSNoteProperty("ProcessName", "code"));
        psoCode.Properties.Add(new PSNoteProperty("CPU", 12.1));
        psoCode.Properties.Add(new PSNoteProperty("RAM_MB", 450.0));

        context.LastOutputObjects = new() { psoChrome, psoCode };
        var execResult = ExecutionResult.CreateSuccess("chrome 14200 48.5\ncode 8320 12.1", TimeSpan.FromMilliseconds(50), context.CurrentDirectory, new() { psoChrome, psoCode });
        context.AddHistory(req2.NaturalLanguageInput, res2, execResult);

        // Turn 3: Follow-up action
        var req3 = new TranslationRequest("Close the one using the most CPU.", context);
        var res3 = await _engine.TranslateAsync(req3);

        Assert.Equal("Stop-Process -Id 14200 -Force", res3.PowerShellScript);
        Assert.True(res3.RequiresExplicitConfirmation);
        Assert.NotNull(res3.ImpactExplanation);
        Assert.Contains("chrome", res3.ImpactExplanation);
        Assert.Contains("14200", res3.ImpactExplanation);
        Assert.Equal(RiskLevel.High, res3.Risk);
        Assert.Equal(IntentCategory.ProcessManagement, res3.Category);
    }

    [Theory]
    [InlineData("grab all mp4 files in downloads", "Get-ChildItem", "*.mp4")]
    [InlineData("hunt down whatever is occupying port 3000", "Get-NetTCPConnection", "3000")]
    [InlineData("fetch the 5 heaviest ram hogs", "Get-Process", "WorkingSet64")]
    [InlineData("nuke process with pid 9090", "Stop-Process -Id 9090 -Force", null)]
    public async Task VariedNaturalEnglish_UnderstoodWithoutRigidPatterns(string input, string expectedScriptSnippet, string? expectedArg)
    {
        var context = new ShellContext();
        var req = new TranslationRequest(input, context);

        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res);
        Assert.Contains(expectedScriptSnippet, res.PowerShellScript, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(expectedArg))
        {
            Assert.Contains(expectedArg, res.PowerShellScript, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FollowUp_OrdinalTarget_SelectsFirstProcessFromPreviousOutput()
    {
        var context = new ShellContext();
        var psoNode = new PSObject();
        psoNode.Properties.Add(new PSNoteProperty("Id", 5432));
        psoNode.Properties.Add(new PSNoteProperty("ProcessName", "node"));

        var psoPython = new PSObject();
        psoPython.Properties.Add(new PSNoteProperty("Id", 6789));
        psoPython.Properties.Add(new PSNoteProperty("ProcessName", "python"));

        context.LastOutputObjects = new() { psoNode, psoPython };
        context.AddHistory("list processes", TranslationResult.CreateSuccess("list processes", "Get-Process", "Lists processes"));

        var req = new TranslationRequest("close the first one", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Equal("Stop-Process -Id 5432 -Force", res.PowerShellScript);
        Assert.True(res.RequiresExplicitConfirmation);
        Assert.Contains("node", res.ImpactExplanation);
    }

    [Fact]
    public async Task FollowUp_SortContinuation_ReordersPreviousQuery()
    {
        var context = new ShellContext();
        context.AddHistory("show running processes", TranslationResult.CreateSuccess("show running processes", "Get-Process | Select-Object -First 10 Id, ProcessName, CPU", "Lists top processes"));

        var req = new TranslationRequest("sort them by memory instead", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Contains("Sort-Object WorkingSet64 -Descending", res.PowerShellScript);
    }

    [Fact]
    public async Task FollowUp_LimitContinuation_AdjustsLimitCount()
    {
        var context = new ShellContext();
        context.AddHistory("show running processes", TranslationResult.CreateSuccess("show running processes", "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Id, ProcessName, CPU", "Lists top processes"));

        var req = new TranslationRequest("show only the top 3", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Contains("-First 3", res.PowerShellScript);
    }

    [Fact]
    public async Task FollowUp_ExportToJson_ExportsStructuredPipelineObjects()
    {
        var context = new ShellContext();
        var pso = new PSObject();
        pso.Properties.Add(new PSNoteProperty("Name", "test.txt"));
        pso.Properties.Add(new PSNoteProperty("Length", 1024L));
        context.LastOutputObjects = new() { pso };
        context.AddHistory("list files", TranslationResult.CreateSuccess("list files", "Get-ChildItem", "Lists files"));

        var req = new TranslationRequest("export that to json", context);
        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res.DirectNativeAction);
        var execResult = res.DirectNativeAction(context);
        Assert.True(execResult.Success);
        Assert.Contains("test.txt", execResult.StandardOutput);
    }

    [Fact]
    public async Task FollowUp_Elevate_WrapsLastCommandWithAdministratorVerb()
    {
        var context = new ShellContext();
        context.AddHistory("stop service wuauserv", TranslationResult.CreateSuccess("stop service wuauserv", "Stop-Service -Name 'wuauserv'", "Stops service"));

        var req = new TranslationRequest("try again with admin", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Contains("Start-Process powershell -Verb RunAs", res.PowerShellScript);
        Assert.Contains("Stop-Service -Name 'wuauserv'", res.PowerShellScript);
        Assert.Equal(RiskLevel.High, res.Risk);
    }

    [Fact]
    public async Task Correction_PortCorrection_UpdatesPortNumber()
    {
        var context = new ShellContext();
        context.AddHistory("check port 8080", TranslationResult.CreateSuccess("check port 8080", "Get-NetTCPConnection -LocalPort 8080", "Checks port 8080", RiskLevel.Safe, IntentCategory.Network));

        var req = new TranslationRequest("no, I meant port 5000", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Contains("-LocalPort 5000", res.PowerShellScript);
        Assert.True(res.IsConversationalResponse);
        Assert.Contains("5000", res.ConversationalMessage);
    }

    [Fact]
    public async Task Correction_PathCorrection_UpdatesSearchPath()
    {
        var context = new ShellContext();
        context.AddHistory("find all log files", TranslationResult.CreateSuccess("find all log files", "Get-ChildItem -Path '.' -Filter '*.log'", "Lists log files", RiskLevel.Safe, IntentCategory.FileSystem));

        var req = new TranslationRequest("actually on drive D:", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Contains("-Path 'D:'", res.PowerShellScript);
        Assert.True(res.IsConversationalResponse);
        Assert.Contains("D:", res.ConversationalMessage);
    }

    [Fact]
    public async Task Correction_TargetProcessCorrection_UpdatesProcessName()
    {
        var context = new ShellContext();
        context.AddHistory("kill python", TranslationResult.CreateSuccess("kill python", "Stop-Process -Name 'python' -Force", "Terminates python", RiskLevel.High, IntentCategory.ProcessManagement));

        var req = new TranslationRequest("not that one, kill chrome instead", context);
        var res = await _engine.TranslateAsync(req);

        Assert.Equal("Stop-Process -Name 'chrome' -Force", res.PowerShellScript);
        Assert.True(res.RequiresExplicitConfirmation);
        Assert.Contains("chrome", res.ImpactExplanation);
    }

    [Fact]
    public async Task MultiTurn_ContextMemory_PreservesTurnsAcrossDialogueSession()
    {
        var context = new ShellContext();

        var req1 = new TranslationRequest("list files", context);
        var res1 = await _engine.TranslateAsync(req1);
        context.AddHistory(req1.NaturalLanguageInput, res1, ExecutionResult.CreateSuccess("file1.txt", TimeSpan.FromMilliseconds(10), context.CurrentDirectory));

        var req2 = new TranslationRequest("what is my ip address", context);
        var res2 = await _engine.TranslateAsync(req2);
        context.AddHistory(req2.NaturalLanguageInput, res2, ExecutionResult.CreateSuccess("192.168.1.100", TimeSpan.FromMilliseconds(10), context.CurrentDirectory));

        Assert.Equal(2, context.ConversationTurns.Count);
        Assert.Equal("list files", context.ConversationTurns[0].UserMessage);
        Assert.Equal("what is my ip address", context.ConversationTurns[1].UserMessage);
    }

    [Fact]
    public async Task MultiTurn_DiskSpaceProblem_ProposalAndAffirmation()
    {
        var context = new ShellContext();

        // Turn 1: Problem statement
        var req1 = new TranslationRequest("I am running out of disk space", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.True(res1.IsConversationalResponse);
        Assert.True(res1.IsPendingProposal);
        Assert.NotNull(res1.PendingProposal);
        Assert.Contains("inspect your hard drives", res1.ConversationalMessage);
        Assert.Equal("Analyzing disk storage", context.ActiveGoal);
        Assert.NotNull(context.ActiveProposal);

        // Turn 2: User says "go ahead"
        var req2 = new TranslationRequest("go ahead", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveProposal);
        Assert.False(res2.IsPendingProposal);
        Assert.Contains("Get-Volume", res2.PowerShellScript);
    }

    [Fact]
    public async Task MultiTurn_ProposalRejection_ClearsProposalSafely()
    {
        var context = new ShellContext();

        // Turn 1: Problem statement
        var req1 = new TranslationRequest("My laptop feels slow.", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveProposal);

        // Turn 2: User rejects
        var req2 = new TranslationRequest("never mind", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveProposal);
        Assert.True(res2.IsConversationalResponse);
        Assert.Contains("cancelled", res2.ConversationalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(res2.PowerShellScript);
    }

    [Fact]
    public async Task Ambiguity_UnspecifiedTarget_PromptsClarifyingQuestion()
    {
        var context = new ShellContext();
        var req = new TranslationRequest("clean temp", context);
        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res.Clarification);
        Assert.Contains("clean", res.Clarification.Question, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(res.Clarification.Options);
        Assert.Contains(res.Clarification.Options, opt => opt.Label.Contains("TEMP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FollowUp_ExportToCsv_ExportsStructuredPipelineObjects()
    {
        var context = new ShellContext();
        var pso = new PSObject();
        pso.Properties.Add(new PSNoteProperty("Name", "test.txt"));
        pso.Properties.Add(new PSNoteProperty("Length", 2048L));
        context.LastOutputObjects = new() { pso };
        context.AddHistory("list files", TranslationResult.CreateSuccess("list files", "Get-ChildItem", "Lists files"));

        var req = new TranslationRequest("save that to csv", context);
        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res.DirectNativeAction);
        var execResult = res.DirectNativeAction(context);
        Assert.True(execResult.Success);
        Assert.Contains("test.txt", execResult.StandardOutput);
    }

    [Theory]
    [InlineData("what is consuming port 8080", "Get-NetTCPConnection", "8080")]
    [InlineData("show me disk volume space", "Get-Volume", "Fixed")]
    [InlineData("look for 10 largest files", "Get-ChildItem", "Length")]
    public async Task VariedNaturalEnglish_AdditionalPhrasings_UnderstoodAccurately(string input, string expectedSnippet, string? expectedArg)
    {
        var context = new ShellContext();
        var req = new TranslationRequest(input, context);
        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res);
        Assert.Contains(expectedSnippet, res.PowerShellScript, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(expectedArg))
        {
            Assert.Contains(expectedArg, res.PowerShellScript, StringComparison.OrdinalIgnoreCase);
        }
    }
}
