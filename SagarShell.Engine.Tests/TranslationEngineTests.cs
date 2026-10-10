using System;
using System.IO;
using System.Linq;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using Xunit;

namespace SagarShell.Engine.Tests;

public class TranslationEngineTests
{
    private readonly NaturalLanguageEngine _engine = new();

    [Theory]
    [InlineData("list files", "Get-ChildItem -Path .")]
    [InlineData("show all files in downloads", "Get-ChildItem -Path \"$HOME\\Downloads\"")]
    [InlineData("find all pdf files", "-Filter '*.pdf'")]
    [InlineData("create a folder called TestProject", "New-Item -ItemType Directory -Path 'TestProject'")]
    [InlineData("create file notes.txt with content 'Hello World'", "Set-Content -Path 'notes.txt' -Value 'Hello World'")]
    [InlineData("read file package.json", "Get-Content -Path 'package.json'")]
    [InlineData("show first 20 lines of app.log", "Get-Content -Path 'app.log' -TotalCount 20")]
    [InlineData("tail 15 lines of error.log", "Get-Content -Path 'error.log' -Tail 15")]
    [InlineData("delete file temp.txt", "Remove-Item -Path 'temp.txt' -Recurse -Force")]
    [InlineData("empty recycle bin", "Clear-RecycleBin -Force")]
    [InlineData("zip folder src to backup.zip", "Compress-Archive -Path 'src' -DestinationPath 'backup.zip'")]
    public void FileSystem_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.FileSystem, result.Category);
    }

    [Theory]
    [InlineData("show running processes", "Get-Process")]
    [InlineData("what is using the most cpu", "Get-Process | Sort-Object CPU -Descending")]
    [InlineData("show top 5 memory hogs", "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 5")]
    [InlineData("kill process chrome", "Stop-Process -Name 'chrome' -Force")]
    [InlineData("kill pid 4580", "Stop-Process -Id 4580 -Force")]
    [InlineData("kill whatever is using port 3000", "Get-NetTCPConnection -LocalPort 3000")]
    [InlineData("what is using port 8080", "Get-NetTCPConnection -LocalPort 8080")]
    public void Process_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.ProcessManagement, result.Category);
    }

    [Theory]
    [InlineData("what is my ip", "Get-NetIPAddress -AddressFamily IPv4")]
    [InlineData("what is my public ip", "https://api.ipify.org")]
    [InlineData("ping google.com", "Test-Connection -TargetName 'google.com' -Count 4")]
    [InlineData("test if port 80 is open on google.com", "Test-NetConnection -ComputerName 'google.com' -Port 80")]
    [InlineData("flush dns", "Clear-DnsClientCache")]
    [InlineData("show open ports", "Get-NetTCPConnection -State Listen")]
    public void Network_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.Network, result.Category);
    }

    [Theory]
    [InlineData("show system info", "Get-ComputerInfo")]
    [InlineData("how much free ram do i have", "Win32_OperatingSystem")]
    [InlineData("cpu usage", "Win32_Processor")]
    [InlineData("system uptime", "LastBootUpTime")]
    [InlineData("show disk space", "Get-Volume")]
    [InlineData("lock computer", "user32.dll,LockWorkStation")]
    [InlineData("restart computer", "shutdown /r /t 0")]
    public void System_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
    }

    [Theory]
    [InlineData("list services", "Get-Service")]
    [InlineData("check service wuauserv", "Get-Service -Name 'wuauserv*'")]
    [InlineData("restart service spooler", "Restart-Service -Name 'spooler' -Force")]
    [InlineData("stop service spooler", "Stop-Service -Name 'spooler' -Force")]
    public void WindowsServices_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.WindowsServices, result.Category);
    }

    [Theory]
    [InlineData("git status", "git status -s")]
    [InlineData("commit everything with message 'initial commit'", "git commit -m \"initial commit\"")]
    [InlineData("create and switch to branch feature/login", "git checkout -b 'feature/login'")]
    [InlineData("show last 5 commits", "git log -n 5")]
    [InlineData("pull latest changes", "git pull")]
    [InlineData("push changes", "git push")]
    [InlineData("stash changes", "git stash")]
    public void Git_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.GitOperations, result.Category);
    }

    [Theory]
    [InlineData("go to desktop", "Set-Location -Path \"$HOME\\Desktop\"")]
    [InlineData("cd downloads", "Set-Location -Path \"$HOME\\Downloads\"")]
    [InlineData("cd ..", "Set-Location -Path ..")]
    public void Navigation_Translations_ShouldMatchPowerShellCommands(string englishQuery, string expectedSnippet)
    {
        var result = _engine.Translate(new TranslationRequest(englishQuery));

        Assert.Contains(expectedSnippet, result.PowerShellScript);
        Assert.Equal(IntentCategory.Navigation, result.Category);
    }

    [Fact]
    public void SafetyAnalyzer_ShouldClassifyRiskAccurately()
    {
        var safeRes = _engine.Translate(new TranslationRequest("list files in desktop"));
        Assert.Equal(RiskLevel.Safe, safeRes.Risk);

        var modRes = _engine.Translate(new TranslationRequest("create folder Sandbox"));
        Assert.Equal(RiskLevel.Moderate, modRes.Risk);

        var highRes = _engine.Translate(new TranslationRequest("kill process node"));
        Assert.Equal(RiskLevel.High, highRes.Risk);

        var criticalRes = _engine.Translate(new TranslationRequest("delete folder temp recursively"));
        Assert.Equal(RiskLevel.Critical, criticalRes.Risk);
    }

    [Fact]
    public void ConversationalPleasantries_ShouldBeSanitized()
    {
        var res1 = _engine.Translate(new TranslationRequest("Hey Sagar, could you please list all files in downloads, thanks?"));
        var res2 = _engine.Translate(new TranslationRequest("list all files in downloads"));

        Assert.Equal(res1.PowerShellScript, res2.PowerShellScript);
    }

    [Fact]
    public void ContextAndFollowUps_ShouldResolvePronounsAndPipelines()
    {
        var context = new ShellContext(@"C:\Users\User\Projects");

        // 1. First step: list files
        var req1 = new TranslationRequest("list all pdf files", context);
        var res1 = _engine.Translate(req1);
        context.AddHistory(req1.NaturalLanguageInput, res1);

        // 2. Follow-up: sort them by size
        var req2 = new TranslationRequest("sort them by size", context);
        var res2 = _engine.Translate(req2);
        context.AddHistory(req2.NaturalLanguageInput, res2);

        Assert.Contains("Sort-Object Length -Descending", res2.PowerShellScript);

        // 3. Follow-up: show top 5
        var req3 = new TranslationRequest("show first 5", context);
        var res3 = _engine.Translate(req3);
        Assert.Contains("Select-Object -First 5", res3.PowerShellScript);

        // 4. Test Process pronoun resolution
        var reqProc = new TranslationRequest("kill whatever is using port 3000", context);
        var resProc = _engine.Translate(reqProc);
        context.AddHistory(reqProc.NaturalLanguageInput, resProc);

        Assert.Equal(3000, context.Memory.LastReferencedPorts.First());
    }
}
