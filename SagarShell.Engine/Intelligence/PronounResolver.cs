using System;
using System.Linq;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Pipeline;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Resolves anaphoric references, pronouns ("it", "them", "that", "the previous one"),
/// in-memory structured object pipelines, and follow-up continuations from ShellContext.
/// </summary>
public static class PronounResolver
{
    private static readonly Regex PronounRegex = new(
        @"\b(?:it|them|those|these|that|the\s+(?:same\s+)?(?:file|folder|dir|directory|process|service|port|repo|one|item))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PipelineContinuationRegex = new(
        @"^(?:sort\s+(?:them\s+|it\s+)?by|filter\s+(?:them\s+|it\s+)?by|show\s+(?:only\s+)?(?:the\s+)?(?:first|top|last)|count\s+(?:them|it)?|select\s+|export\s+(?:them\s+|it\s+|that\s+)?to\s+|convert\s+to\s+|inspect\s+)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExportRegex = new(
        @"^(?:export|convert|save)\s+(?:them\s+|it\s+|that\s+|the\s+results?\s+)?(?:to|as)\s+(?<format>json|csv|xml|table)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InspectRegex = new(
        @"(?:inspect|show\s+details\s+of|examine)\s+(?:the\s+)?(?:first|second|third|\d+(?:st|nd|rd|th)?|last)?\s*(?:item|one|object|result)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsPronounOrContinuation(string input)
    {
        return PronounRegex.IsMatch(input)
            || PipelineContinuationRegex.IsMatch(input)
            || ExportRegex.IsMatch(input)
            || InspectRegex.IsMatch(input);
    }

    public static TranslationResult? TryResolveFollowUp(string cleanInput, ShellContext context)
    {
        if (context.History.Count == 0)
            return null;

        var lastEntry = context.History.Last();
        var lastTranslation = lastEntry.Translation;
        var lower = cleanInput.ToLowerInvariant().Trim();

        // 1. In-Memory Structured Object Pipeline Exports (JSON / CSV / Table)
        var exportMatch = ExportRegex.Match(lower);
        if (exportMatch.Success)
        {
            var format = exportMatch.Groups["format"].Value.ToLowerInvariant();
            if (format == "json")
            {
                var res = TranslationResult.CreateSuccess(
                    cleanInput,
                    $"{lastTranslation.PowerShellScript} | ConvertTo-Json -Depth 5",
                    "Exports structured result pipeline to JSON format.",
                    RiskLevel.Safe,
                    lastTranslation.Category
                );
                res.DirectNativeAction = ctx =>
                {
                    if (ctx.LastOutputObjects.Count > 0)
                    {
                        var json = StructuredObjectPipeline.ExportToJson(ctx.LastOutputObjects);
                        return ExecutionResult.CreateSuccess(json, TimeSpan.Zero, ctx.CurrentDirectory);
                    }
                    return ExecutionResult.CreateFailure("No in-memory structured objects found from previous command.", 1, TimeSpan.Zero);
                };
                return res;
            }

            if (format == "csv")
            {
                var res = TranslationResult.CreateSuccess(
                    cleanInput,
                    $"{lastTranslation.PowerShellScript} | ConvertTo-Csv -NoTypeInformation",
                    "Exports structured result pipeline to CSV format.",
                    RiskLevel.Safe,
                    lastTranslation.Category
                );
                res.DirectNativeAction = ctx =>
                {
                    if (ctx.LastOutputObjects.Count > 0)
                    {
                        var csv = StructuredObjectPipeline.ExportToCsv(ctx.LastOutputObjects);
                        return ExecutionResult.CreateSuccess(csv, TimeSpan.Zero, ctx.CurrentDirectory);
                    }
                    return ExecutionResult.CreateFailure("No in-memory structured objects found from previous command.", 1, TimeSpan.Zero);
                };
                return res;
            }
        }

        // 2. In-Memory Object Inspection
        if (InspectRegex.IsMatch(lower))
        {
            int index = 0;
            if (lower.Contains("second") || lower.Contains(" 2")) index = 1;
            else if (lower.Contains("third") || lower.Contains(" 3")) index = 2;
            else if (lower.Contains("last")) index = Math.Max(0, context.LastOutputObjects.Count - 1);

            var res = TranslationResult.CreateSuccess(
                cleanInput,
                $"({lastTranslation.PowerShellScript})[{index}] | Format-List *",
                $"Inspects all detailed properties and methods of item #{index + 1}.",
                RiskLevel.Safe,
                lastTranslation.Category
            );

            res.DirectNativeAction = ctx =>
            {
                if (ctx.LastOutputObjects.Count > index)
                {
                    var targetObj = ctx.LastOutputObjects[index];
                    var report = StructuredObjectPipeline.Inspect(targetObj);
                    return ExecutionResult.CreateSuccess(report.ToString(), TimeSpan.Zero, ctx.CurrentDirectory);
                }
                return ExecutionResult.CreateFailure($"Item at index {index} not found in in-memory object cache.", 1, TimeSpan.Zero);
            };

            return res;
        }

        // 3. Pipeline Continuations (e.g. "sort by size", "sort them by date descending", "show top 5", "count them")
        if (lastTranslation.Category == IntentCategory.FileSystem || lastTranslation.Category == IntentCategory.ProcessManagement || lastTranslation.Category == IntentCategory.WindowsServices)
        {
            if (lower.StartsWith("sort by size") || lower.StartsWith("sort them by size") || lower.StartsWith("order by size"))
            {
                var isDesc = !lower.Contains("ascending");
                var prop = lastTranslation.Category == IntentCategory.ProcessManagement ? "WorkingSet64" : "Length";
                var ps = $"{lastTranslation.PowerShellScript} | Sort-Object {prop} {(isDesc ? "-Descending" : "")}";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Sorted previous {lastTranslation.Category} results by size {(isDesc ? "descending" : "ascending")}.", RiskLevel.Safe, lastTranslation.Category);
            }

            if (lower.StartsWith("sort by date") || lower.StartsWith("sort them by date") || lower.StartsWith("sort by time") || lower.StartsWith("order by date"))
            {
                var isDesc = !lower.Contains("ascending") && !lower.Contains("oldest");
                var ps = $"{lastTranslation.PowerShellScript} | Sort-Object LastWriteTime {(isDesc ? "-Descending" : "")}";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Sorted previous items by modification date {(isDesc ? "newest first" : "oldest first")}.", RiskLevel.Safe, lastTranslation.Category);
            }

            if (lower.StartsWith("sort by name") || lower.StartsWith("sort them alphabetically"))
            {
                var ps = $"{lastTranslation.PowerShellScript} | Sort-Object Name";
                return TranslationResult.CreateSuccess(cleanInput, ps, "Sorted previous items by name.", RiskLevel.Safe, lastTranslation.Category);
            }

            // Select columns: "select Name, Id", "show only columns Name, CPU"
            var selectMatch = Regex.Match(lower, @"^(?:select|show\s+(?:only\s+)?columns?|project)\s+(?<cols>.+)$", RegexOptions.IgnoreCase);
            if (selectMatch.Success)
            {
                var cols = selectMatch.Groups["cols"].Value.Trim();
                var ps = $"{lastTranslation.PowerShellScript} | Select-Object {cols}";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Projects columns ({cols}) from previous query.", RiskLevel.Safe, lastTranslation.Category);
            }

            // Top N filter: "show only top 5", "show first 10", "take 5"
            var topMatch = Regex.Match(lower, @"^(?:show\s+(?:only\s+)?|take\s+|select\s+)?(?:top|first)\s+(?<count>\d+)", RegexOptions.IgnoreCase);
            if (topMatch.Success)
            {
                var count = topMatch.Groups["count"].Value;
                var ps = $"{lastTranslation.PowerShellScript} | Select-Object -First {count}";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Showing first {count} items from the previous query.", RiskLevel.Safe, lastTranslation.Category);
            }

            if (lower == "count them" || lower == "how many are there" || lower == "count")
            {
                var ps = $"({lastTranslation.PowerShellScript} | Measure-Object).Count";
                return TranslationResult.CreateSuccess(cleanInput, ps, "Counting total items from previous query.", RiskLevel.Safe, lastTranslation.Category);
            }
        }

        // 4. Pronoun actions on Process (e.g. "kill it", "stop it", "terminate it")
        if (lower is "kill it" or "stop it" or "terminate it" or "kill them" or "end it" or "kill that process")
        {
            if (context.Memory.LastReferencedProcessIds.Count > 0)
            {
                var pids = string.Join(", ", context.Memory.LastReferencedProcessIds);
                var ps = $"Stop-Process -Id {pids} -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Terminating process with PID(s): {pids}.", RiskLevel.High, IntentCategory.ProcessManagement);
            }

            if (context.Memory.LastReferencedProcessNames.Count > 0)
            {
                var names = string.Join(", ", context.Memory.LastReferencedProcessNames.Select(n => $"'{n}'"));
                var ps = $"Stop-Process -Name {names} -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Terminating process: {names}.", RiskLevel.High, IntentCategory.ProcessManagement);
            }

            if (context.Memory.LastReferencedServices.Count > 0)
            {
                var sName = context.Memory.LastReferencedServices.First();
                var ps = $"Stop-Service -Name '{sName}' -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Stopping Windows service: '{sName}'.", RiskLevel.High, IntentCategory.WindowsServices);
            }
        }

        // 5. Pronoun actions on Service (e.g. "restart it", "start it")
        if (lower is "restart it" or "restart that service")
        {
            if (context.Memory.LastReferencedServices.Count > 0)
            {
                var sName = context.Memory.LastReferencedServices.First();
                var ps = $"Restart-Service -Name '{sName}' -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Restarting Windows service: '{sName}'.", RiskLevel.High, IntentCategory.WindowsServices);
            }
        }

        // 6. Pronoun actions on Files / Paths (e.g. "delete it", "delete them", "remove them", "open it", "how big is it")
        if (lower is "delete it" or "delete them" or "remove it" or "remove them" or "delete that" or "delete those files")
        {
            if (context.Memory.LastReferencedPaths.Count > 0)
            {
                var paths = string.Join(", ", context.Memory.LastReferencedPaths.Select(NaturalPathResolver.EscapeForPowerShell));
                var ps = $"Remove-Item -Path {paths} -Recurse -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Deleting previously referenced item(s): {paths}.", RiskLevel.Critical, IntentCategory.FileSystem);
            }

            if (lastTranslation.Category == IntentCategory.FileSystem && lastTranslation.PowerShellScript.Contains("Get-ChildItem"))
            {
                // Delete everything matched by previous query
                var ps = $"{lastTranslation.PowerShellScript} | Remove-Item -Recurse -Force";
                return TranslationResult.CreateSuccess(cleanInput, ps, "Deleting all items matched by the previous search query.", RiskLevel.Critical, IntentCategory.FileSystem);
            }
        }

        if (lower is "open it" or "launch it" or "open that file" or "edit it")
        {
            if (context.Memory.LastReferencedPaths.Count > 0)
            {
                var target = NaturalPathResolver.EscapeForPowerShell(context.Memory.LastReferencedPaths.First());
                var ps = $"Invoke-Item -Path {target}";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Opening {target} in the default application.", RiskLevel.Moderate, IntentCategory.FileSystem);
            }
        }

        if (lower is "how big is it" or "what is its size" or "size of it")
        {
            if (context.Memory.LastReferencedPaths.Count > 0)
            {
                var target = NaturalPathResolver.EscapeForPowerShell(context.Memory.LastReferencedPaths.First());
                var ps = $"Get-ChildItem -Path {target} -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum | Select-Object @{{Name='SizeMB'; Expression={{[math]::Round($_.Sum / 1MB, 2)}}}}, Count";
                return TranslationResult.CreateSuccess(cleanInput, ps, $"Calculating total size of {target}.", RiskLevel.Safe, IntentCategory.FileSystem);
            }
        }

        return null;
    }
}
