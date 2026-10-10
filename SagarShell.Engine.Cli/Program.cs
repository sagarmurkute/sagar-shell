using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SagarShell.Engine.Cli.Rendering;
using SagarShell.Engine.Diagnosis;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Memory;
using SagarShell.Engine.Models;
using SagarShell.Engine.Planning;
using Spectre.Console;

namespace SagarShell.Engine.Cli;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var engine = new NaturalLanguageEngine();
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var memoryManager = new PersistentMemoryManager(Directory.GetCurrentDirectory(), context.IsElevated);

        TerminalRenderer.RenderBanner(context, executor);

        while (true)
        {
            var shortPath = ShortenPath(context.CurrentDirectory);
            AnsiConsole.Markup($"\n[bold cyan]SagarShell[/] [dim]{Markup.Escape(shortPath)}[/] [bold white]>[/] ");

            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var trimmed = input.Trim();

            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                await memoryManager.SaveActiveSessionAsync();
                AnsiConsole.MarkupLine("[bold yellow]Session saved. Exiting Sagar Shell. Goodbye![/]");
                break;
            }

            if (trimmed.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                TerminalRenderer.RenderHistory(context.History);
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

            var planner = new MultiStepTaskPlanner(engine);

            // Check if this is a compound multi-step instruction
            if (planner.IsCompoundInstruction(trimmed))
            {
                var plan = planner.CreatePlan(trimmed, context);
                TerminalRenderer.RenderPlanSummary(plan);

                var proceed = AnsiConsole.Confirm("\n⚡ Proceed with this multi-step execution plan?", defaultValue: true);
                if (!proceed)
                {
                    AnsiConsole.MarkupLine("[bold darkorange]Plan execution cancelled by user.[/]");
                    continue;
                }

                AnsiConsole.MarkupLine("\n[dim]─── Executing Multi-Step Plan ───[/]");

                PlanExecutionReport report = null!;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan bold"))
                    .StartAsync("Executing plan...", async ctx =>
                    {
                        report = await planner.ExecutePlanAsync(
                            plan,
                            executor,
                            context,
                            confirmationHandler: async step =>
                            {
                                return AnsiConsole.Confirm(
                                    $"\n⚠️ Step {step.StepIndex + 1} modifies state: \"{step.Description}\"\nConfirm this step?",
                                    defaultValue: true);
                            });
                    });

                if (report.Success)
                {
                    AnsiConsole.MarkupLine($"\n[bold green]{Markup.Escape(report.GenerateSummary())}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"\n[bold red]{Markup.Escape(report.GenerateSummary())}[/]");
                }
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

            // Handle clarification or conversational turn responses
            if (translation.NeedsClarification && translation.Clarification != null)
            {
                var clar = translation.Clarification;
                TerminalRenderer.RenderClarification(clar);

                AnsiConsole.Markup($"[bold cyan]Select an option [[1-{clar.Options.Count}]] or type your choice (press Enter to cancel):[/] ");
                var choiceStr = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(choiceStr) || choiceStr.Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    context.ActiveClarification = null;
                    AnsiConsole.MarkupLine("[bold darkorange]Clarification cancelled.[/]");
                    continue;
                }

                // Resolve user choice with ClarificationEngine
                translation = ClarificationEngine.ResolveClarification(choiceStr, context);
                if (translation.IsConversationalResponse)
                {
                    TerminalRenderer.RenderConversationalResponse(translation);
                }
                else
                {
                    TerminalRenderer.RenderTranslationSummary(translation);
                }
            }
            else if (translation.IsConversationalResponse)
            {
                TerminalRenderer.RenderConversationalResponse(translation);

                // If this is a pending proposal awaiting user answer, wait for next user input
                if (translation.IsPendingProposal)
                {
                    context.RecordTurn(trimmed, translation, null);
                    continue;
                }
            }
            else
            {
                TerminalRenderer.RenderTranslationSummary(translation);
            }

            // If empty or unrecognized command
            if (string.IsNullOrWhiteSpace(translation.PowerShellScript))
            {
                context.RecordTurn(trimmed, translation, null);
                continue;
            }

            // Explicit confirmation prompt with impact explanation
            if (translation.RequiresExplicitConfirmation)
            {
                var confirm = AnsiConsole.Confirm("[bold yellow]⚡ Do you want to proceed with this action?[/]", defaultValue: false);
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[bold darkorange]Action cancelled by user.[/]");
                    continue;
                }
            }
            // Safety prompt for Critical risk operations
            else if (translation.Risk == RiskLevel.Critical && !isDryRun)
            {
                var confirm = AnsiConsole.Confirm("[bold red]⚠️ CRITICAL OPERATION: Are you sure you want to execute this?[/]", defaultValue: false);
                if (!confirm)
                {
                    AnsiConsole.MarkupLine("[bold darkorange]Execution aborted by user.[/]");
                    continue;
                }
            }

            // Execute the operation with interactive spinner
            ExecutionResult result = null!;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan bold"))
                .StartAsync("Executing command...", async _ =>
                {
                    result = await executor.ExecuteAsync(translation, context);
                });

            AnsiConsole.MarkupLine("\n[dim]─── Output ───[/]");

            // If structured object pipeline result is available and non-empty, render structured table
            if (result.HasStructuredData && result.StructuredObjects != null && result.StructuredObjects.Count > 0)
            {
                TerminalRenderer.RenderStructuredPipelineResult(result.StructuredObjects);
            }
            else if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                Console.WriteLine(result.StandardOutput);
            }

            if (!result.Success)
            {
                if (!string.IsNullOrEmpty(result.StandardError))
                {
                    AnsiConsole.MarkupLine($"[bold red][Error][/] {Markup.Escape(result.StandardError)}");
                }

                if (result.Diagnosis != null)
                {
                    TerminalRenderer.RenderDiagnosis(result.Diagnosis);

                    if (result.Diagnosis.CanBeRemediated)
                    {
                        var fixChoice = AnsiConsole.Confirm("Apply suggested auto-recovery fix and retry?", defaultValue: true);
                        if (fixChoice)
                        {
                            var recoveryManager = new RecoveryManager();
                            RecoveryResult recovery = null!;

                            await AnsiConsole.Status()
                                .Spinner(Spinner.Known.Dots)
                                .SpinnerStyle(Style.Parse("gold1 bold"))
                                .StartAsync("Applying remediation...", async _ =>
                                {
                                    recovery = await recoveryManager.ApplyRecoveryAsync(
                                        result.Diagnosis,
                                        executor,
                                        context,
                                        approvalPrompt: _ => Task.FromResult(true));
                                });

                            if (recovery.RetrySucceeded || recovery.RemediationSucceeded)
                            {
                                AnsiConsole.MarkupLine($"[bold green][Recovery][/] {Markup.Escape(recovery.SummaryMessage)}");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[bold red][Recovery][/] {Markup.Escape(recovery.SummaryMessage)}");
                            }

                            if (recovery.RetryOutput != null && !string.IsNullOrEmpty(recovery.RetryOutput.StandardOutput))
                            {
                                Console.WriteLine(recovery.RetryOutput.StandardOutput);
                            }
                        }
                    }
                }
            }

            memoryManager.RecordExecution(trimmed, translation.OriginalInput, translation, result, context);
            AnsiConsole.MarkupLine($"[dim]─── Done ({result.Duration.TotalMilliseconds:F0} ms) ───[/]");
        }
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
        AnsiConsole.MarkupLine("\n" + SagarShell.Engine.Intelligence.Patterns.HelpPatterns.BuildCapabilitiesSummary());
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
