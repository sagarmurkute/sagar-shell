using System;
using System.IO;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Cli;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var engine = new NaturalLanguageEngine();
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var memoryManager = new SagarShell.Engine.Memory.PersistentMemoryManager(Directory.GetCurrentDirectory(), context.IsElevated);

        PrintBanner(context, executor);

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"\n[SagarShell] ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{ShortenPath(context.CurrentDirectory)}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" > ");
            Console.ResetColor();

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var trimmed = input.Trim();

            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                await memoryManager.SaveActiveSessionAsync();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Session saved. Exiting Sagar Shell. Goodbye!");
                Console.ResetColor();
                break;
            }

            if (trimmed.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                PrintHistory(context, memoryManager);
                continue;
            }

            if (IsHelpOrCapabilitiesCommand(trimmed))
            {
                PrintHelp();
                continue;
            }

            bool isDryRun = false;
            if (trimmed.StartsWith("--dry-run ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("dry ", StringComparison.OrdinalIgnoreCase))
            {
                isDryRun = true;
                trimmed = trimmed.StartsWith("--dry-run ", StringComparison.OrdinalIgnoreCase)
                    ? trimmed["--dry-run ".Length..].Trim()
                    : trimmed["dry ".Length..].Trim();
            }

            var planner = new SagarShell.Engine.Planning.MultiStepTaskPlanner(engine);

            // Check if this is a compound multi-step instruction
            if (planner.IsCompoundInstruction(trimmed))
            {
                var plan = planner.CreatePlan(trimmed, context);
                PrintPlanSummary(plan);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("\n⚡ Proceed with this multi-step execution plan? (Y/n/dry): ");
                Console.ResetColor();
                var planConfirm = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (planConfirm == "dry")
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("[Dry Run] Plan validated. No commands executed.");
                    Console.ResetColor();
                    continue;
                }

                if (!string.IsNullOrEmpty(planConfirm) && planConfirm != "y" && planConfirm != "yes")
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Plan execution cancelled by user.");
                    Console.ResetColor();
                    continue;
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\n--- Executing Multi-Step Plan ---");
                Console.ResetColor();

                var report = await planner.ExecutePlanAsync(
                    plan,
                    executor,
                    context,
                    confirmationHandler: async step =>
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"\n⚠️  Step {step.StepIndex + 1} modifies state: \"{step.Description}\"\n    Confirm this step? (Y/n): ");
                        Console.ResetColor();
                        var stepAns = Console.ReadLine()?.Trim().ToLowerInvariant();
                        return string.IsNullOrEmpty(stepAns) || stepAns == "y" || stepAns == "yes";
                    });

                Console.ForegroundColor = report.Success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"\n{report.GenerateSummary()}");
                Console.ResetColor();
                continue;
            }

            // Single step instruction translation
            var request = new TranslationRequest
            {
                NaturalLanguageInput = trimmed,
                Context = context,
                EnableDryRun = isDryRun
            };

            var translation = await engine.TranslateAsync(request);

            // Handle interactive clarification if input is ambiguous
            if (translation.NeedsClarification && translation.Clarification != null)
            {
                var clar = translation.Clarification;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n🤔 {clar.Question}");
                Console.ResetColor();

                for (int i = 0; i < clar.Options.Count; i++)
                {
                    var opt = clar.Options[i];
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"  [{opt.Index}] ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"{opt.Label}: ");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"{opt.Description}");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"\nSelect an option [1-{clar.Options.Count}] or press Enter to cancel: ");
                Console.ResetColor();
                var choiceStr = Console.ReadLine()?.Trim();
                if (int.TryParse(choiceStr, out int choice) && choice >= 1 && choice <= clar.Options.Count)
                {
                    var selectedOpt = clar.Options[choice - 1];
                    translation = TranslationResult.CreateSuccess(
                        selectedOpt.Label,
                        selectedOpt.PowerShellScript,
                        selectedOpt.Description,
                        selectedOpt.Risk,
                        selectedOpt.Category
                    );
                    translation.RequiresElevation = selectedOpt.RequiresElevation;
                    PrintTranslationSummary(translation);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Clarification cancelled.");
                    Console.ResetColor();
                    continue;
                }
            }
            else
            {
                // Display translation breakdown
                PrintTranslationSummary(translation);
            }

            // If empty or unrecognized
            if (string.IsNullOrWhiteSpace(translation.PowerShellScript))
            {
                continue;
            }

            // Safety prompt for High / Critical risk operations
            if (translation.Risk == RiskLevel.Critical && !isDryRun)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("\n⚠️  CRITICAL OPERATION: Are you sure you want to execute this? (y/N/dry): ");
                Console.ResetColor();
                var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (confirm == "dry")
                {
                    request.EnableDryRun = true;
                    translation = await engine.TranslateAsync(request);
                    PrintTranslationSummary(translation);
                }
                else if (confirm != "y" && confirm != "yes")
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Execution aborted by user.");
                    Console.ResetColor();
                    continue;
                }
            }

            // Execute the operation
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n--- Execution Output ---");
            Console.ResetColor();

            var result = await executor.ExecuteAsync(translation, context);

            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(result.StandardOutput);
                Console.ResetColor();
            }

            if (!result.Success)
            {
                if (!string.IsNullOrEmpty(result.StandardError))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Error] {result.StandardError}");
                    Console.ResetColor();
                }

                if (result.Diagnosis != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n🔍 [Diagnosis: {result.Diagnosis.Category}]");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"   • {result.Diagnosis.PlainEnglishSummary}");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"   • Likely Cause: {result.Diagnosis.LikelyCause}");
                    Console.ResetColor();

                    if (result.Diagnosis.CanBeRemediated)
                    {
                        var fix = result.Diagnosis.SuggestedFix!;
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"\n💡 Suggested Fix: {fix.Description}");
                        if (!string.IsNullOrEmpty(fix.RemediationScript))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"   Command: {fix.RemediationScript}");
                        }
                        Console.ResetColor();

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("   Apply suggested fix & retry? (y/N): ");
                        Console.ResetColor();
                        var fixChoice = Console.ReadLine()?.Trim().ToLowerInvariant();

                        if (fixChoice == "y" || fixChoice == "yes")
                        {
                            var recoveryManager = new SagarShell.Engine.Diagnosis.RecoveryManager();
                            var recovery = await recoveryManager.ApplyRecoveryAsync(
                                result.Diagnosis,
                                executor,
                                context,
                                approvalPrompt: _ => Task.FromResult(true));

                            Console.ForegroundColor = recovery.RetrySucceeded || recovery.RemediationSucceeded ? ConsoleColor.Green : ConsoleColor.Red;
                            Console.WriteLine($"\n[Recovery] {recovery.SummaryMessage}");
                            if (recovery.RetryOutput != null && !string.IsNullOrEmpty(recovery.RetryOutput.StandardOutput))
                            {
                                Console.ForegroundColor = ConsoleColor.Gray;
                                Console.WriteLine(recovery.RetryOutput.StandardOutput);
                            }
                            Console.ResetColor();
                        }
                    }
                }
            }

            memoryManager.RecordExecution(trimmed, translation.OriginalInput, translation, result, context);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"--- Done ({result.Duration.TotalMilliseconds:F0} ms) ---");
            Console.ResetColor();
        }
    }

    private static void PrintBanner(ShellContext context, ShellRuntimeManager runtime)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   _____                         _____ _          _ _ 
  / ____|                       / ____| |        | | |
 | (___   __ _  __ _  __ _ _ __| (___ | |__   ___| | |
  \___ \ / _` |/ _` |/ _` | '__|\___ \| '_ \ / _ \ | |
  ____) | (_| | (_| | (_| | |   ____) | | | |  __/ | |
 |_____/ \__,_|\__, |\__,_|_|  |_____/|_| |_|\___|_|_|
                __/ |                                 
               |___/     Natural Language Shell Engine");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("===============================================================");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("Type any instruction in plain English (or type 'help', 'history', 'exit').");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Session Directory : {context.CurrentDirectory}");
        Console.WriteLine($"Elevation State   : {(context.IsElevated ? "Administrator (Elevated)" : "Standard User")}");
        Console.WriteLine($"Execution Runtime : {(runtime.IsRunspaceActive ? "In-Process PowerShell Runspace (Persistent State)" : "Out-of-Process Fallback")}");
        Console.WriteLine("===============================================================\n");
        Console.ResetColor();
    }

    private static void PrintTranslationSummary(TranslationResult result)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("\n[Translation] ");

        // Risk badge
        var (riskColor, riskBadge) = result.Risk switch
        {
            RiskLevel.Safe => (ConsoleColor.Green, "[SAFE]"),
            RiskLevel.Low => (ConsoleColor.Cyan, "[LOW]"),
            RiskLevel.Moderate => (ConsoleColor.Yellow, "[MODERATE]"),
            RiskLevel.High => (ConsoleColor.Magenta, "[HIGH RISK]"),
            RiskLevel.Critical => (ConsoleColor.Red, "[CRITICAL]"),
            _ => (ConsoleColor.White, "[UNKNOWN]")
        };

        Console.ForegroundColor = riskColor;
        Console.Write($"{riskBadge} ");

        if (result.RequiresElevation)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ADMIN REQUIRED] ");
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(result.Category.ToString());

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  PowerShell  : ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(result.PowerShellScript);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Explanation : ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(result.Explanation);

        if (!string.IsNullOrEmpty(result.RiskExplanation))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  Risk Note   : ");
            Console.ForegroundColor = riskColor;
            Console.WriteLine(result.RiskExplanation);
        }

        if (result.SuggestedFollowUps.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  Suggestions : ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(string.Join(" | ", result.SuggestedFollowUps));
        }

        Console.ResetColor();
    }

    private static void PrintPlanSummary(SagarShell.Engine.Planning.ExecutionPlan plan)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n[Multi-Step Execution Plan] {plan.TotalSteps} Steps | Overall Risk: {plan.OverallRisk}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('=', 65));

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var (riskColor, riskBadge) = step.Risk switch
            {
                RiskLevel.Safe => (ConsoleColor.Green, "[SAFE]"),
                RiskLevel.Low => (ConsoleColor.Cyan, "[LOW]"),
                RiskLevel.Moderate => (ConsoleColor.Yellow, "[MODERATE]"),
                RiskLevel.High => (ConsoleColor.Magenta, "[HIGH RISK]"),
                RiskLevel.Critical => (ConsoleColor.Red, "[CRITICAL]"),
                _ => (ConsoleColor.White, "[UNKNOWN]")
            };

            var deps = step.DependsOnStepIndices.Count > 0
                ? $" (Depends on Step {string.Join(", ", step.DependsOnStepIndices.Select(d => d + 1))})"
                : "";

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  Step {i + 1}: ");
            Console.ForegroundColor = riskColor;
            Console.Write($"{riskBadge} ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{step.Description}{deps}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("    PowerShell: ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(step.Translation.PowerShellScript);
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(new string('=', 65));
        Console.ResetColor();
    }

    private static void PrintHistory(ShellContext context, SagarShell.Engine.Memory.PersistentMemoryManager memoryManager)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Command History ---");
        Console.ResetColor();

        var activeCommands = memoryManager.ActiveSession.Commands;
        if (activeCommands.Count == 0 && context.History.Count == 0)
        {
            Console.WriteLine("No commands recorded yet in this session.");
            return;
        }

        for (int i = 0; i < activeCommands.Count; i++)
        {
            var h = activeCommands[i];
            var status = h.Success ? "[✓]" : "[✗]";
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{i + 1}] {h.Timestamp:HH:mm:ss} ");
            Console.ForegroundColor = h.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write($"{status} ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"\"{h.RawInput}\"");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" -> ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(h.PowerShellScript);
        }
        Console.ResetColor();
    }

    private static bool IsHelpOrCapabilitiesCommand(string input)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "help" or "?" or "/help" or "--help" or "-h" or "man"
            or "what can you do" or "what can you do?" or "what can you do for me"
            or "what can i do" or "what do you do" or "what are your capabilities"
            or "capabilities" or "what are you capable of" or "what can this shell do"
            or "tell me what you can do" or "commands" or "show commands"
            or "examples" or "show examples" or "features" or "show features";
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + SagarShell.Engine.Intelligence.Patterns.HelpPatterns.BuildCapabilitiesSummary());
        Console.ResetColor();
    }

    private static string ShortenPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[home.Length..];
        }
        return path;
    }
}
