using System;
using System.Linq;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class ConfidenceClarificationTests
{
    private readonly NaturalLanguageEngine _engine;

    public ConfidenceClarificationTests()
    {
        _engine = new NaturalLanguageEngine();
    }

    [Theory]
    [InlineData("clean temp", "What would you like to clean?")]
    [InlineData("delete files", "What files would you like to delete, and from where?")]
    [InlineData("kill process", "Which process would you like to target?")]
    [InlineData("stop service", "Which Windows service would you like to inspect or manage?")]
    [InlineData("check connection", "Where would you like to test network connectivity to?")]
    [InlineData("build project", "Which project build tool would you like to use?")]
    [InlineData("check ports", "Which port or connection check would you like to perform?")]
    [InlineData("backup project", "How would you like to back up this project?")]
    [InlineData("reset", "What would you like to reset?")]
    [InlineData("kill python", "Multiple 'python' instances may be active. How would you like to proceed?")]
    public async Task AmbiguousRequests_PromptClarifyingQuestion_WithoutBlindExecution(string input, string expectedQuestionSnippet)
    {
        var context = new ShellContext();
        var req = new TranslationRequest(input, context);

        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res.Clarification);
        Assert.True(res.NeedsClarification);
        Assert.Contains(expectedQuestionSnippet, res.Clarification.Question, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(res.Clarification.Options);
        Assert.NotNull(context.ActiveClarification);
        Assert.True(res.Confidence < 0.75, $"Confidence should be below threshold for ambiguous input '{input}'");
    }

    [Fact]
    public async Task DanglingPronoun_WithoutContext_PromptsClarification()
    {
        var context = new ShellContext();
        var req = new TranslationRequest("kill it", context);

        var res = await _engine.TranslateAsync(req);

        Assert.NotNull(res.Clarification);
        Assert.Contains("There is no active item in context", res.Clarification.Question);
    }

    [Fact]
    public async Task Clarification_OptionSelectionByNumber_ExecutesSelectedOption()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("clean temp", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: User selects option 1 (User temp)
        var req2 = new TranslationRequest("1", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveClarification);
        Assert.Contains("$env:TEMP", res2.PowerShellScript);
        Assert.Contains("Remove-Item", res2.PowerShellScript);
        Assert.True(res2.Confidence >= 0.90);
    }

    [Fact]
    public async Task Clarification_OptionSelectionByKeyword_MatchesCorrectOption()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("clean temp", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: User responds with "recycle bin"
        var req2 = new TranslationRequest("the recycle bin", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveClarification);
        Assert.Contains("Clear-RecycleBin", res2.PowerShellScript);
    }

    [Fact]
    public async Task Clarification_BackupProject_GitOptionByKeyword()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("backup project", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: User selects Git checkpoint
        var req2 = new TranslationRequest("git checkpoint", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveClarification);
        Assert.Contains("git commit", res2.PowerShellScript);
    }

    [Fact]
    public async Task Clarification_RefinedSemanticCorrection_OverridesWithCustomCommand()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("delete files", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: User clarifies with specific path and extension
        var req2 = new TranslationRequest("actually only pdf files in downloads", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveClarification);
        Assert.Contains("Get-ChildItem", res2.PowerShellScript);
        Assert.Contains("*.pdf", res2.PowerShellScript);
        Assert.Contains("Remove-Item", res2.PowerShellScript);
    }

    [Fact]
    public async Task Clarification_Cancellation_SafelyAbortsWithoutExecution()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("delete files", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: User cancels
        var req2 = new TranslationRequest("never mind", context);
        var res2 = await _engine.TranslateAsync(req2);

        Assert.Null(context.ActiveClarification);
        Assert.True(res2.IsConversationalResponse);
        Assert.Contains("cancelled", res2.ConversationalMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(res2.PowerShellScript);
    }

    [Fact]
    public async Task Clarification_UnclearResponse_PromptsForOptionRetry()
    {
        var context = new ShellContext();

        // Turn 1: Ambiguous prompt
        var req1 = new TranslationRequest("clean temp", context);
        var res1 = await _engine.TranslateAsync(req1);

        Assert.NotNull(context.ActiveClarification);

        // Turn 2: Gibberish / non-matching answer
        var req2 = new TranslationRequest("xyzzy banana", context);
        var res2 = await _engine.TranslateAsync(req2);

        // Clarification should still remain active
        Assert.NotNull(context.ActiveClarification);
        Assert.NotNull(res2.Clarification);
        Assert.Contains("select an option", res2.ConversationalMessage, StringComparison.OrdinalIgnoreCase);
    }
}
