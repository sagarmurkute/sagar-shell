using System;
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

        // -------------------------------------------------------------
        // Turn 1: User expresses problem: "My laptop feels slow."
        // -------------------------------------------------------------
        var req1 = new TranslationRequest("My laptop feels slow.", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.True(res1.IsConversationalResponse);
        Assert.True(res1.IsPendingProposal);
        Assert.NotNull(res1.PendingProposal);
        Assert.Contains("check running processes and resource usage", res1.ConversationalMessage);
        Assert.Equal("Diagnosing system slowdown", context.ActiveGoal);
        Assert.NotNull(context.ActiveProposal);

        // Record turn into conversation history
        context.RecordTurn(req1.NaturalLanguageInput, res1, null);

        // -------------------------------------------------------------
        // Turn 2: User responds: "Yes."
        // -------------------------------------------------------------
        var req2 = new TranslationRequest("Yes.", context);
        var res2 = await _engine.TranslateAsync(req2);

        // Proposal is activated and cleared from pending
        Assert.Null(context.ActiveProposal);
        Assert.False(res2.IsPendingProposal);
        Assert.Contains("Get-Process", res2.PowerShellScript);
        Assert.Contains("Sort-Object CPU -Descending", res2.PowerShellScript);

        // Simulate execution output returning structured PSObjects (Chrome PID 14200 with 48.5s CPU)
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

        // -------------------------------------------------------------
        // Turn 3: User instructs: "Close the one using the most CPU."
        // -------------------------------------------------------------
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
    [InlineData("My computer is lagging badly")]
    [InlineData("Why is everything so slow?")]
    [InlineData("PC feels sluggish")]
    public async Task Conversational_VariationsOfSystemSlow_UnderstoodAsDiagnosticProposal(string input)
    {
        var context = new ShellContext();
        var req = new TranslationRequest(input, context);

        var res = await _engine.TranslateAsync(req);

        Assert.True(res.IsConversationalResponse);
        Assert.True(res.IsPendingProposal);
        Assert.Contains("check running processes and resource usage", res.ConversationalMessage);
    }

    [Theory]
    [InlineData("sure")]
    [InlineData("go ahead")]
    [InlineData("do it")]
    [InlineData("please do")]
    [InlineData("let's do it")]
    public async Task Conversational_AffirmationVariations_ActivatePendingProposal(string affirmation)
    {
        var context = new ShellContext();
        
        // Setup pending proposal
        var req1 = new TranslationRequest("laptop is slow", context);
        await _engine.TranslateAsync(req1);
        Assert.NotNull(context.ActiveProposal);

        // Send affirmation
        var req2 = new TranslationRequest(affirmation, context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveProposal);
        Assert.Contains("Get-Process", res2.PowerShellScript);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("cancel")]
    [InlineData("never mind")]
    [InlineData("don't")]
    public async Task Conversational_Rejection_CancelsProposalWithoutExecuting(string rejection)
    {
        var context = new ShellContext();

        // Setup pending proposal
        var req1 = new TranslationRequest("laptop is slow", context);
        await _engine.TranslateAsync(req1);
        Assert.NotNull(context.ActiveProposal);

        // Send rejection
        var req2 = new TranslationRequest(rejection, context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveProposal);
        Assert.Empty(res2.PowerShellScript);
        Assert.True(res2.IsConversationalResponse);
        Assert.Contains("cancelled", res2.ConversationalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiTurn_DiskSpaceProblem_And_DeleteLargestFile()
    {
        var context = new ShellContext();

        // 1. Problem
        var req1 = new TranslationRequest("I'm running out of disk space", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.True(res1.IsPendingProposal);
        Assert.Contains("inspect your hard drives", res1.ConversationalMessage);

        // 2. Affirm
        var req2 = new TranslationRequest("Go ahead", context);
        var res2 = await _engine.TranslateAsync(req2);
        Assert.Contains("Get-Volume", res2.PowerShellScript);

        // Setup mock file objects
        var psoBigVideo = new PSObject();
        psoBigVideo.Properties.Add(new PSNoteProperty("FullName", "C:\\Videos\\recording.mp4"));
        psoBigVideo.Properties.Add(new PSNoteProperty("Name", "recording.mp4"));
        psoBigVideo.Properties.Add(new PSNoteProperty("Length", 5242880000L)); // 5GB

        context.LastOutputObjects = new() { psoBigVideo };

        // 3. Follow-up: delete largest file
        var req3 = new TranslationRequest("Delete the largest file", context);
        var res3 = await _engine.TranslateAsync(req3);

        Assert.Equal("Remove-Item -Path 'C:\\Videos\\recording.mp4' -Force", res3.PowerShellScript);
        Assert.True(res3.RequiresExplicitConfirmation);
        Assert.NotNull(res3.ImpactExplanation);
        Assert.Contains("recording.mp4", res3.ImpactExplanation);
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
}
