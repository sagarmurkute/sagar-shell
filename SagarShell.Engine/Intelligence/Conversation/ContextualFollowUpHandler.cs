using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Pipeline;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Conversation;

/// <summary>
/// Handles multi-turn conversational follow-up instructions referencing prior results,
/// ordinals ("first one", "2nd one"), sorting modifications, limits, and exports.
/// </summary>
public static class ContextualFollowUpHandler
{
    private static readonly Regex OrdinalTargetRegex = new(
        @"\b(?:close|kill|terminate|stop|end|open|view|inspect|delete|remove)\s+(?:the\s+)?(?<ord>first|1st|second|2nd|third|3rd|fourth|4th|fifth|5th|\d+th|\d+st|\d+nd|\d+rd)\s+(?:one|process|file|item|entry)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SortContinuationRegex = new(
        @"\b(?:sort|order)\s+(?:them\s+|it\s+)?by\s+(?<col>memory|ram|cpu|size|date|time|name)(?:\s+instead)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LimitContinuationRegex = new(
        @"\b(?:show|display|limit\s+to|only)\s+(?:the\s+)?(?:top|first)?\s*(?<num>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExportContinuationRegex = new(
        @"\b(?:export|save)\s+(?:that|this|them|results?)\s+(?:to|as)\s+(?<format>json|csv|text)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ElevateContinuationRegex = new(
        @"\b(?:try\s+(?:again\s+)?with\s+admin|elevate\s+(?:that|it|command)|run\s+as\s+admin(?:istrator)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RestartPronounRegex = new(
        @"\b(?:restart|bounce)\s+(?:it|that|the\s+service)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool CanHandle(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var lower = input.ToLowerInvariant().Trim();
        return OrdinalTargetRegex.IsMatch(lower)
            || SortContinuationRegex.IsMatch(lower)
            || LimitContinuationRegex.IsMatch(lower)
            || ExportContinuationRegex.IsMatch(lower)
            || ElevateContinuationRegex.IsMatch(lower)
            || RestartPronounRegex.IsMatch(lower);
    }

    public static TranslationResult? TryHandle(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var lower = input.ToLowerInvariant().Trim();

        // 1. Ordinal Target: "close the first one", "kill 2nd process"
        var ordMatch = OrdinalTargetRegex.Match(lower);
        if (ordMatch.Success && context.LastOutputObjects != null && context.LastOutputObjects.Count > 0)
        {
            var ordStr = ordMatch.Groups["ord"].Value.ToLowerInvariant();
            int index = ParseOrdinalToIndex(ordStr);

            if (index >= 0 && index < context.LastOutputObjects.Count)
            {
                var targetObj = context.LastOutputObjects[index];
                return GenerateOrdinalAction(input, lower, targetObj, index + 1, context);
            }
        }

        // 2. Sort Continuation: "sort them by memory instead"
        var sortMatch = SortContinuationRegex.Match(lower);
        if (sortMatch.Success && context.History.Count > 0)
        {
            var colKey = sortMatch.Groups["col"].Value.ToLowerInvariant();
            var sortProp = colKey switch
            {
                "memory" or "ram" => "WorkingSet64",
                "cpu" => "CPU",
                "size" => "Length",
                "date" or "time" => "LastWriteTime",
                "name" => "Name",
                _ => "CPU"
            };

            var lastCmd = context.History[^1].Translation.PowerShellScript;
            if (lastCmd.Contains("Get-Process", StringComparison.OrdinalIgnoreCase))
            {
                var script = $"Get-Process | Sort-Object {sortProp} -Descending | Select-Object -First 10 Id, ProcessName, @{{Name='RAM_MB';Expression={{[math]::Round($_.WorkingSet64/1MB,1)}}}}, CPU";
                return TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Re-runs process query sorted by {colKey} descending.",
                    RiskLevel.Safe,
                    IntentCategory.ProcessManagement
                );
            }
            else if (lastCmd.Contains("Get-ChildItem", StringComparison.OrdinalIgnoreCase))
            {
                var script = $"Get-ChildItem -File | Sort-Object {sortProp} -Descending | Select-Object Name, @{{Name='Size_MB';Expression={{[math]::Round($_.Length/1MB,2)}}}}, LastWriteTime";
                return TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Re-runs file listing sorted by {colKey} descending.",
                    RiskLevel.Safe,
                    IntentCategory.FileSystem
                );
            }
        }

        // 3. Limit Continuation: "show only the top 5"
        var limitMatch = LimitContinuationRegex.Match(lower);
        if (limitMatch.Success && context.History.Count > 0)
        {
            var countStr = limitMatch.Groups["num"].Value;
            if (int.TryParse(countStr, out int count))
            {
                var lastCmd = context.History[^1].Translation.PowerShellScript;
                string newScript;

                if (lastCmd.Contains("Select-Object -First", StringComparison.OrdinalIgnoreCase))
                {
                    newScript = Regex.Replace(lastCmd, @"-First\s+\d+", $"-First {count}", RegexOptions.IgnoreCase);
                }
                else
                {
                    newScript = $"{lastCmd} | Select-Object -First {count}";
                }

                return TranslationResult.CreateSuccess(
                    input,
                    newScript,
                    $"Limits results of previous command to top {count}.",
                    RiskLevel.Safe,
                    context.History[^1].Translation.Category
                );
            }
        }

        // 4. Export Continuation: "export that to json"
        var exportMatch = ExportContinuationRegex.Match(lower);
        if (exportMatch.Success && context.LastOutputObjects != null && context.LastOutputObjects.Count > 0)
        {
            var fmt = exportMatch.Groups["format"].Value.ToLowerInvariant();
            if (fmt == "json")
            {
                var res = TranslationResult.CreateSuccess(
                    input,
                    "$context.LastOutputObjects | ConvertTo-Json -Depth 3",
                    "Exports preserved in-memory objects to JSON format.",
                    RiskLevel.Safe,
                    IntentCategory.GeneralQuery
                );
                res.DirectNativeAction = ctx =>
                {
                    var json = StructuredObjectPipeline.ExportToJson(ctx.LastOutputObjects);
                    return ExecutionResult.CreateSuccess(json, TimeSpan.FromMilliseconds(5), ctx.CurrentDirectory, ctx.LastOutputObjects.Cast<object>().ToList(), "InProcessStructuredPipeline");
                };
                return res;
            }
            else if (fmt == "csv")
            {
                var res = TranslationResult.CreateSuccess(
                    input,
                    "$context.LastOutputObjects | ConvertTo-Csv -NoTypeInformation",
                    "Exports preserved in-memory objects to CSV format.",
                    RiskLevel.Safe,
                    IntentCategory.GeneralQuery
                );
                res.DirectNativeAction = ctx =>
                {
                    var csv = StructuredObjectPipeline.ExportToCsv(ctx.LastOutputObjects);
                    return ExecutionResult.CreateSuccess(csv, TimeSpan.FromMilliseconds(5), ctx.CurrentDirectory, ctx.LastOutputObjects.Cast<object>().ToList(), "InProcessStructuredPipeline");
                };
                return res;
            }
        }

        // 5. Elevate Continuation: "try again with admin"
        if (ElevateContinuationRegex.IsMatch(lower) && context.History.Count > 0)
        {
            var lastCmd = context.History[^1].Translation.PowerShellScript;
            var script = $"Start-Process powershell -Verb RunAs -ArgumentList \"-NoExit\", \"-Command\", \"{lastCmd.Replace("\"", "`\"")}\"";
            
            return TranslationResult.CreateSuccess(
                input,
                script,
                "Executes the previous command in an elevated Administrator PowerShell session.",
                RiskLevel.High,
                context.History[^1].Translation.Category
            );
        }

        // 6. Restart Pronoun: "restart it"
        if (RestartPronounRegex.IsMatch(lower) && context.Memory.LastReferencedServices.Count > 0)
        {
            var sName = context.Memory.LastReferencedServices[^1];
            return TranslationResult.CreateSuccess(
                input,
                $"Restart-Service -Name '{sName}' -Force; Get-Service -Name '{sName}'",
                $"Restarts Windows service '{sName}'.",
                RiskLevel.High,
                IntentCategory.WindowsServices
            );
        }

        return null;
    }

    private static int ParseOrdinalToIndex(string ord)
    {
        return ord switch
        {
            "first" or "1st" => 0,
            "second" or "2nd" => 1,
            "third" or "3rd" => 2,
            "fourth" or "4th" => 3,
            "fifth" or "5th" => 4,
            _ => int.TryParse(Regex.Match(ord, @"\d+").Value, out int n) ? n - 1 : -1
        };
    }

    private static TranslationResult GenerateOrdinalAction(string rawInput, string lower, PSObject obj, int displayIndex, ShellContext context)
    {
        // 1. Process target
        int pid = 0;
        if (obj.Properties["Id"]?.Value is int idVal) pid = idVal;
        else if (int.TryParse(obj.Properties["Id"]?.Value?.ToString(), out int pId)) pid = pId;

        string name = obj.Properties["ProcessName"]?.Value?.ToString()
            ?? obj.Properties["Name"]?.Value?.ToString() ?? "";

        if (pid > 0 || !string.IsNullOrEmpty(name))
        {
            if (lower.Contains("kill") || lower.Contains("close") || lower.Contains("terminate") || lower.Contains("stop"))
            {
                var script = pid > 0 ? $"Stop-Process -Id {pid} -Force" : $"Stop-Process -Name '{name}' -Force";
                var res = TranslationResult.CreateSuccess(
                    rawInput,
                    script,
                    $"Terminates entry #{displayIndex}: '{name}' (PID {pid}).",
                    RiskLevel.High,
                    IntentCategory.ProcessManagement
                );
                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Closing '{name}' (PID {pid}) will immediately terminate this process.";
                res.IsConversationalResponse = true;
                res.ConversationalMessage = $"Selected item #{displayIndex}: '{name}' (PID {pid}). Confirm below to terminate.";
                return res;
            }
        }

        // 2. File target
        var fullName = obj.Properties["FullName"]?.Value?.ToString();
        if (!string.IsNullOrEmpty(fullName))
        {
            if (lower.Contains("delete") || lower.Contains("remove"))
            {
                var res = TranslationResult.CreateSuccess(
                    rawInput,
                    $"Remove-Item -Path '{fullName}' -Force",
                    $"Deletes entry #{displayIndex}: '{fullName}'.",
                    RiskLevel.High,
                    IntentCategory.FileSystem
                );
                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Permanently deletes '{fullName}'.";
                res.IsConversationalResponse = true;
                return res;
            }
            else
            {
                return TranslationResult.CreateSuccess(
                    rawInput,
                    $"Invoke-Item -Path '{fullName}'",
                    $"Opens entry #{displayIndex}: '{fullName}'.",
                    RiskLevel.Safe,
                    IntentCategory.FileSystem
                );
            }
        }

        if (lower.Contains("inspect") || lower.Contains("view") || lower.Contains("properties") || lower.Contains("details") || lower.Contains("examine"))
        {
            var res = TranslationResult.CreateSuccess(
                rawInput,
                $"$context.LastOutputObjects[{displayIndex - 1}] | Format-List *",
                $"Inspects properties of entry #{displayIndex}.",
                RiskLevel.Safe,
                IntentCategory.GeneralQuery
            );
            res.DirectNativeAction = ctx =>
            {
                var report = StructuredObjectPipeline.Inspect(obj);
                return ExecutionResult.CreateSuccess(report.ToString(), TimeSpan.FromMilliseconds(5), ctx.CurrentDirectory);
            };
            return res;
        }

        return TranslationResult.CreateSuccess(
            rawInput,
            $"# Selected item #{displayIndex}",
            $"References item #{displayIndex}.",
            RiskLevel.Safe,
            IntentCategory.GeneralQuery
        );
    }
}
