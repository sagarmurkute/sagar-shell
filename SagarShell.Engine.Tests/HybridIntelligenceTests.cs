using System;
using System.Threading.Tasks;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Intelligence.AIProviders;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class HybridIntelligenceTests
{
    private readonly NaturalLanguageEngine _engine;

    public HybridIntelligenceTests()
    {
        _engine = new NaturalLanguageEngine();
    }

    [Theory]
    [InlineData("clean temp", "What would you like to clean?")]
    [InlineData("kill python", "Multiple 'python' instances may be active")]
    [InlineData("backup project", "How would you like to back up this project?")]
    [InlineData("delete logs", "Where would you like to delete log files from?")]
    [InlineData("reset", "What would you like to reset?")]
    public void AmbiguityDetector_FlagsAmbiguousCommandsWithClarificationQuestions(string input, string expectedQuestionFragment)
    {
        var context = new ShellContext();
        var request = new TranslationRequest(input, context);

        var result = _engine.Translate(request);

        Assert.True(result.NeedsClarification);
        Assert.NotNull(result.Clarification);
        Assert.Contains(expectedQuestionFragment, result.Clarification.Question);
        Assert.NotEmpty(result.Clarification.Options);
    }

    [Fact]
    public void ClarificationOption_HasExecutablePowerShellAndDescriptions()
    {
        var context = new ShellContext();
        var request = new TranslationRequest("clean temp", context);

        var result = _engine.Translate(request);

        Assert.NotNull(result.Clarification);
        var options = result.Clarification.Options;
        Assert.True(options.Count >= 2);

        foreach (var option in options)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Label));
            Assert.False(string.IsNullOrWhiteSpace(option.PowerShellScript));
            Assert.False(string.IsNullOrWhiteSpace(option.Description));
        }
    }

    [Fact]
    public void AIOutputValidator_AcceptsValidPowerShellScripts()
    {
        var validJson = """
        {
            "powerShellScript": "Get-Process | Where-Object { $_.CPU -gt 10 } | Select-Object Name, CPU",
            "explanation": "Finds high CPU processes.",
            "category": "ProcessManagement"
        }
        """;
        var validation = AIOutputValidator.ValidateAndParse(validJson, "find heavy processes");

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.ValidatedTranslation);
        Assert.Equal("Get-Process | Where-Object { $_.CPU -gt 10 } | Select-Object Name, CPU", validation.ValidatedTranslation.PowerShellScript);
        Assert.Equal(IntentCategory.ProcessManagement, validation.ValidatedTranslation.Category);
    }

    [Fact]
    public void AIOutputValidator_RejectsMalformedPowerShellSyntax()
    {
        var malformedJson = """
        {
            "powerShellScript": "Get-Process | Where-Object { $_.CPU -gt 10 ",
            "explanation": "Broken script"
        }
        """; // Unclosed brace
        var validation = AIOutputValidator.ValidateAndParse(malformedJson, "broken query");

        Assert.False(validation.IsValid);
        Assert.Contains("PowerShell syntax error", validation.ErrorReason);
    }

    [Fact]
    public void AIOutputValidator_FlagsHighRiskDestructiveOperations()
    {
        var destructiveJson = """
        {
            "powerShellScript": "del /s /q c:\\windows",
            "explanation": "Deletes Windows directory"
        }
        """;
        var validation = AIOutputValidator.ValidateAndParse(destructiveJson, "delete windows");

        Assert.False(validation.IsValid);
        Assert.Contains("unsafe destructive operation", validation.ErrorReason);
    }

    [Fact]
    public void AIOutputValidator_ParsesValidJsonAIResponse()
    {
        var aiJson = """
        {
            "powerShellScript": "Get-ChildItem -Path $env:TEMP -Recurse",
            "explanation": "Lists temporary files in the user Temp directory.",
            "risk": 0,
            "category": "FileSystem"
        }
        """;

        var validation = AIOutputValidator.ValidateAndParse(aiJson, "list temp files");

        Assert.True(validation.IsValid);
        Assert.NotNull(validation.ValidatedTranslation);
        Assert.Contains("Get-ChildItem", validation.ValidatedTranslation.PowerShellScript);
        Assert.Equal(RiskLevel.Safe, validation.ValidatedTranslation.Risk);
        Assert.Equal(IntentCategory.FileSystem, validation.ValidatedTranslation.Category);
    }

    [Fact]
    public void AIOutputValidator_RejectsInvalidJsonAIResponseWhenMalformed()
    {
        var invalidJson = "{ invalid json without quotes ";

        var validation = AIOutputValidator.ValidateAndParse(invalidJson, "do something");

        Assert.False(validation.IsValid);
        Assert.Null(validation.ValidatedTranslation);
        Assert.Contains("Malformed AI output", validation.ErrorReason);
    }

    [Fact]
    public async Task NaturalLanguageEngine_UsesMockAIProvider_WhenPatternNotFound()
    {
        var mockAI = new MockLLMProvider("""
        {
            "powerShellScript": "Get-FileHash -Path 'thesis.pdf' -Algorithm SHA256",
            "explanation": "Calculates SHA256 checksum of thesis.pdf.",
            "category": "FileSystem"
        }
        """);

        var engine = new NaturalLanguageEngine(mockAI);
        var request = new TranslationRequest("calculate complex cryptography hash digest of my custom file thesis.pdf");

        var result = await engine.TranslateAsync(request);

        Assert.False(result.NeedsClarification);
        Assert.Equal("Get-FileHash -Path 'thesis.pdf' -Algorithm SHA256", result.PowerShellScript);
        Assert.Equal(IntentCategory.FileSystem, result.Category);
    }

    [Fact]
    public async Task NaturalLanguageEngine_GracefullyHandlesAIProviderFailures()
    {
        var failingAI = new FailingLLMProvider();
        var engine = new NaturalLanguageEngine(failingAI);

        var request = new TranslationRequest("completely unprecedented alien instruction");

        var result = await engine.TranslateAsync(request);

        Assert.NotNull(result);
        // Falls back to safe unknown response without throwing an unhandled exception
        Assert.True(result.Confidence == 0.0 || string.IsNullOrEmpty(result.PowerShellScript));
    }

    private class MockLLMProvider : ILLMProvider
    {
        private readonly string _responseJson;
        public string ProviderName => "MockProvider";
        public bool IsAvailable => true;

        public MockLLMProvider(string responseJson)
        {
            _responseJson = responseJson;
        }

        public Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, System.Threading.CancellationToken cancellationToken = default)
        {
            var validation = AIOutputValidator.ValidateAndParse(_responseJson, naturalQuery, context);
            return Task.FromResult(validation.IsValid ? validation.ValidatedTranslation : null);
        }
    }

    private class FailingLLMProvider : ILLMProvider
    {
        public string ProviderName => "FailingProvider";
        public bool IsAvailable => false;

        public Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, System.Threading.CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("AI service connection failed.");
        }
    }
}
