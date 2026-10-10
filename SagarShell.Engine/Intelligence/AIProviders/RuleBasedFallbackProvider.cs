using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.AIProviders;

/// <summary>
/// Heuristic semantic slot-filler that analyzes unknown natural language commands and composes valid PowerShell pipelines.
/// </summary>
public class RuleBasedFallbackProvider : ILLMProvider
{
    public string ProviderName => "Semantic Slot Resolver";
    public bool IsAvailable => true;

    public Task<TranslationResult?> TranslateAsync(string naturalQuery, ShellContext? context, CancellationToken cancellationToken = default)
    {
        var lower = naturalQuery.ToLowerInvariant().Trim();

        // Check for direct PowerShell commands first (e.g. Get-*, Set-*, New-*, etc.)
        if (Regex.IsMatch(lower, @"^(?:get|set|new|remove|start|stop|restart|test|select|where|invoke|measure|sort|clear|write)-[a-z]+", RegexOptions.IgnoreCase))
        {
            var result = TranslationResult.CreateSuccess(
                naturalQuery,
                naturalQuery,
                "Direct PowerShell cmdlet detected.",
                RiskLevel.Moderate,
                IntentCategory.CustomScript,
                0.95
            );
            return Task.FromResult<TranslationResult?>(result);
        }

        // Decompose verbs & targets
        bool isFindOrList = lower.Contains("find") || lower.Contains("list") || lower.Contains("show") || lower.Contains("search") || lower.Contains("get");
        bool isDelete = lower.Contains("delete") || lower.Contains("remove") || lower.Contains("kill");
        bool isCount = lower.Contains("count") || lower.Contains("how many");

        bool targetFile = lower.Contains("file") || lower.Contains("document") || lower.Contains("folder") || lower.Contains("dir") || lower.Contains("item");
        bool targetProc = lower.Contains("process") || lower.Contains("app") || lower.Contains("task") || lower.Contains("program");
        bool targetNet = lower.Contains("port") || lower.Contains("ip") || lower.Contains("connection") || lower.Contains("network");

        // If target is files
        if (targetFile || isFindOrList)
        {
            var size = NaturalSizeParser.TryParse(naturalQuery);
            var time = NaturalTimeParser.TryParse(naturalQuery);

            string ps = "Get-ChildItem -Path .";
            if (size != null || time != null)
            {
                var filters = new System.Collections.Generic.List<string>();
                if (size != null) filters.Add(size.PowerShellFilter);
                if (time != null) filters.Add(time.PowerShellExpression);
                ps += $" | Where-Object {{ {string.Join(" -and ", filters)} }}";
            }

            if (isCount)
            {
                ps = $"({ps} | Measure-Object).Count";
            }
            else if (isDelete)
            {
                ps += " | Remove-Item -Recurse -Force";
            }
            else
            {
                ps += " | Select-Object Name, Length, LastWriteTime";
            }

            var result = TranslationResult.CreateSuccess(
                naturalQuery,
                ps,
                $"Semantic translation for '{naturalQuery}'.",
                isDelete ? RiskLevel.Critical : RiskLevel.Safe,
                IntentCategory.FileSystem,
                0.80
            );
            return Task.FromResult<TranslationResult?>(result);
        }

        // If target is processes
        if (targetProc)
        {
            string ps = "Get-Process";
            if (isDelete)
            {
                ps += " | Stop-Process -Force";
            }
            else
            {
                ps += " | Select-Object -First 20 Id, ProcessName, CPU, WorkingSet64";
            }

            var result = TranslationResult.CreateSuccess(
                naturalQuery,
                ps,
                $"Semantic process translation for '{naturalQuery}'.",
                isDelete ? RiskLevel.High : RiskLevel.Safe,
                IntentCategory.ProcessManagement,
                0.75
            );
            return Task.FromResult<TranslationResult?>(result);
        }

        return Task.FromResult<TranslationResult?>(null);
    }
}
