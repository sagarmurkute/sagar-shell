using System;
using System.Text;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles help, capabilities discovery, and example query requests ("what can you do", "help", "capabilities").
/// </summary>
public class HelpPatterns : ICommandPattern
{
    public int Priority => 95;

    private static readonly Regex WhatCanYouDoRegex = new(
        @"^(?:what\s+can\s+(?:you|i)\s+do(?:\s+for\s+me)?|what\s+do\s+you\s+do|what\s+are\s+your\s+capabilities|capabilities|what\s+are\s+you\s+capable\s+of|what\s+can\s+this\s+shell\s+do|tell\s+me\s+what\s+you\s+can\s+do)\??$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HelpCommandRegex = new(
        @"^(?:help|help\s+me|show\s+help|get\s+help|\?|\/help|--help|-h|man)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExamplesRegex = new(
        @"^(?:show\s+commands|list\s+commands|commands|examples|show\s+examples|show\s+features|features)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return WhatCanYouDoRegex.IsMatch(lower)
            || HelpCommandRegex.IsMatch(lower)
            || ExamplesRegex.IsMatch(lower);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var guide = BuildCapabilitiesSummary();

        var result = TranslationResult.CreateSuccess(
            input,
            "Write-Host 'Sagar Shell: Plain English Capabilities & Help'",
            "Displays Sagar Shell capabilities, supported natural language commands, and usage examples.",
            RiskLevel.Safe,
            IntentCategory.SystemInformation
        );

        result.DirectNativeAction = ctx => ExecutionResult.CreateSuccess(guide, TimeSpan.Zero, ctx.CurrentDirectory);

        result.SuggestedFollowUps.Add("list files in downloads");
        result.SuggestedFollowUps.Add("show top 5 memory hogs");
        result.SuggestedFollowUps.Add("what is using port 3000");
        result.SuggestedFollowUps.Add("Find old videos in Downloads, move them to Archive, then compress them");

        return result;
    }

    public static string BuildCapabilitiesSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                     SAGAR SHELL - WHAT CAN I DO?                            ║");
        sb.AppendLine("║     A Plain English Natural Language Alternative to Windows PowerShell      ║");
        sb.AppendLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine("You can type instructions naturally without memorizing complex syntax or flags.");
        sb.AppendLine();
        sb.AppendLine("📂 1. FILE SYSTEM & STORAGE");
        sb.AppendLine("  • list files in downloads / show top 5 largest files in current folder");
        sb.AppendLine("  • find all .pdf files / find old videos in downloads / find images in pictures");
        sb.AppendLine("  • create a folder called Projects / make a file notes.txt with content 'Hello'");
        sb.AppendLine("  • search for 'TODO' in all .cs files / read file package.json");
        sb.AppendLine("  • show first 20 lines of app.log / tail 30 lines of error.log");
        sb.AppendLine("  • copy folder src to backup / move temp.txt to archive / empty recycle bin");
        sb.AppendLine("  • zip folder src into archive.zip / extract archive.zip into dist");
        sb.AppendLine("  • how big is C:\\Users / calculate size of node_modules");
        sb.AppendLine();
        sb.AppendLine("⚡ 2. PROCESS & PORT MANAGEMENT");
        sb.AppendLine("  • show running processes / what is using the most cpu");
        sb.AppendLine("  • show top 5 memory hogs / find process chrome");
        sb.AppendLine("  • kill process chrome / kill pid 1234 / kill all node processes");
        sb.AppendLine("  • what is using port 3000 / check port 8080 / kill whatever is using port 3000");
        sb.AppendLine();
        sb.AppendLine("⛓️ 3. MULTI-STEP COMPOUND TASK PLANNING");
        sb.AppendLine("  • Find old videos in Downloads, move them to Archive, then compress them");
        sb.AppendLine("  • create folder Backup and copy *.log to Backup then zip Backup");
        sb.AppendLine("  • make directory temp; touch temp\\test.txt; show files in temp");
        sb.AppendLine();
        sb.AppendLine("🧠 4. PRONOUN INTELLIGENCE & STRUCTURED OBJECT PIPELINE");
        sb.AppendLine("  • Type 'list all pdf files', then 'sort them by size', then 'take first 3'");
        sb.AppendLine("  • Type 'what is using port 8080', then 'kill it'");
        sb.AppendLine("  • Type 'show running processes', then 'export to json' or 'export to csv'");
        sb.AppendLine("  • Type 'get services', then 'inspect first item' (deep property introspection)");
        sb.AppendLine();
        sb.AppendLine("🌐 5. NETWORKING & SYSTEM DIAGNOSTICS");
        sb.AppendLine("  • what is my ip / what is my public ip / ping google.com / test connection to github.com");
        sb.AppendLine("  • show open ports / flush dns / show saved wifi passwords");
        sb.AppendLine("  • system specs / free ram / cpu usage / disk space / battery status / uptime");
        sb.AppendLine("  • show env variables / get path variable / set env API_KEY to 'xyz'");
        sb.AppendLine();
        sb.AppendLine("⚙️ 6. WINDOWS SERVICES & APPS");
        sb.AppendLine("  • show running services / status of service spooler");
        sb.AppendLine("  • start service wuauserv / restart service spooler / stop service bits");
        sb.AppendLine("  • open chrome / open notepad / open calculator / launch visual studio code");
        sb.AppendLine("  • show open windows / close window spotify / minimize all windows");
        sb.AppendLine();
        sb.AppendLine("🐙 7. GIT & NAVIGATION");
        sb.AppendLine("  • git status / commit everything with message 'feature ready' / show last 5 commits");
        sb.AppendLine("  • git pull / git push / create branch feature-test / switch to main");
        sb.AppendLine("  • go to desktop / cd .. / go back / clear screen / history");
        sb.AppendLine();
        sb.AppendLine("🔒 8. POWER & LOCK CONTROLS");
        sb.AppendLine("  • lock screen / put pc to sleep / restart computer / shutdown in 10 minutes / cancel shutdown");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        return sb.ToString().TrimEnd();
    }
}
