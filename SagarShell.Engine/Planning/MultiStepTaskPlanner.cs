using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Planning;

/// <summary>
/// Breaks compound natural language instructions into ordered, validated, dependent execution plans.
/// Enforces pre-step validation, user confirmation for modifying actions, and fail-safe abortion on errors.
/// </summary>
public class MultiStepTaskPlanner
{
    private readonly NaturalLanguageEngine _engine;

    public MultiStepTaskPlanner(NaturalLanguageEngine? engine = null)
    {
        _engine = engine ?? new NaturalLanguageEngine();
    }

    /// <summary>
    /// Checks if an instruction contains multiple chained tasks.
    /// </summary>
    public bool IsCompoundInstruction(string input)
    {
        return CompoundInstructionSplitter.IsCompound(input);
    }

    /// <summary>
    /// Decomposes a compound instruction into an ordered, dependent ExecutionPlan.
    /// </summary>
    public ExecutionPlan CreatePlan(string compoundQuery, ShellContext context)
    {
        var plan = new ExecutionPlan
        {
            OriginalInstruction = compoundQuery
        };

        var rawSteps = CompoundInstructionSplitter.Split(compoundQuery);
        if (rawSteps.Count == 0)
        {
            rawSteps.Add(compoundQuery);
        }

        // Clone context for planning-time entity flow simulation
        var simContext = new ShellContext(context.CurrentDirectory)
        {
            IsElevated = context.IsElevated
        };

        for (int i = 0; i < rawSteps.Count; i++)
        {
            var rawStep = rawSteps[i];
            var request = new TranslationRequest(rawStep, simContext);
            var translation = _engine.Translate(request);

            var step = new TaskStep
            {
                StepIndex = i,
                RawInstruction = rawStep,
                Translation = translation,
                RequiresConfirmation = translation.Risk >= RiskLevel.Moderate
            };

            // Steps depend on the immediate prior step by default
            if (i > 0)
            {
                step.DependsOnStepIndices.Add(i - 1);
            }

            // Attach validation rules
            step.Validator = (ctx, st) => ValidateStepPreconditions(ctx, st, plan);

            plan.Steps.Add(step);

            // Record simulation history so downstream steps can resolve "them", "it", etc.
            simContext.AddHistory(rawStep, translation);
        }

        return plan;
    }

    /// <summary>
    /// Executes the plan step-by-step with pre-validation, confirmation prompts, and safe stopping on failure.
    /// </summary>
    public async Task<PlanExecutionReport> ExecutePlanAsync(
        ExecutionPlan plan,
        IShellExecutor executor,
        ShellContext context,
        Func<TaskStep, Task<bool>>? confirmationHandler = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        plan.Status = PlanStatus.InProgress;

        for (int i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var stepSw = Stopwatch.StartNew();

            // 1. Check dependencies
            foreach (var depIdx in step.DependsOnStepIndices)
            {
                var depStep = plan.GetStep(depIdx);
                if (depStep == null || depStep.Status != StepStatus.Succeeded)
                {
                    step.Status = StepStatus.Skipped;
                    step.FailureReason = $"Dependency Step {depIdx + 1} did not succeed (Status: {depStep?.Status}).";
                    break;
                }
            }

            if (step.Status == StepStatus.Skipped)
            {
                continue;
            }

            // 2. Pre-execution Validation
            if (step.Validator != null)
            {
                var validation = step.Validator(context, step);
                if (!validation.IsValid)
                {
                    step.Status = StepStatus.Failed;
                    step.FailureReason = validation.ErrorMessage ?? "Pre-execution validation failed.";
                    stepSw.Stop();
                    step.Duration = stepSw.Elapsed;

                    // Skip subsequent steps
                    SkipRemainingSteps(plan, i + 1, $"Aborted due to Step {i + 1} validation failure: {step.FailureReason}");
                    break;
                }
            }

            // 3. User Confirmation for Modifying Actions
            if (step.RequiresConfirmation && confirmationHandler != null)
            {
                bool confirmed = await confirmationHandler(step);
                if (!confirmed)
                {
                    step.Status = StepStatus.Aborted;
                    step.FailureReason = "User denied confirmation for this modifying action.";
                    stepSw.Stop();
                    step.Duration = stepSw.Elapsed;

                    // Skip remaining steps safely
                    SkipRemainingSteps(plan, i + 1, $"Aborted because Step {i + 1} was cancelled by user.");
                    break;
                }
            }

            // 4. Execution
            step.Status = StepStatus.InProgress;
            try
            {
                var result = await executor.ExecuteAsync(step.Translation, context, cancellationToken);
                stepSw.Stop();
                step.Duration = stepSw.Elapsed;
                step.Result = result;

                if (result.Success)
                {
                    step.Status = StepStatus.Succeeded;
                }
                else
                {
                    step.Status = StepStatus.Failed;
                    step.FailureReason = !string.IsNullOrEmpty(result.StandardError)
                        ? result.StandardError
                        : "Command returned non-zero exit code.";

                    // Safe stop: abort subsequent dependent steps
                    SkipRemainingSteps(plan, i + 1, $"Aborted because Step {i + 1} failed.");
                    break;
                }
            }
            catch (Exception ex)
            {
                stepSw.Stop();
                step.Duration = stepSw.Elapsed;
                step.Status = StepStatus.Failed;
                step.FailureReason = $"Unhandled execution exception: {ex.Message}";

                SkipRemainingSteps(plan, i + 1, $"Aborted due to Step {i + 1} exception.");
                break;
            }
        }

        sw.Stop();

        bool overallSuccess = plan.Steps.All(s => s.Status == StepStatus.Succeeded);
        plan.Status = overallSuccess ? PlanStatus.Completed : PlanStatus.Failed;

        return new PlanExecutionReport
        {
            Plan = plan,
            Success = overallSuccess,
            TotalDuration = sw.Elapsed,
            AbortReason = plan.Steps.FirstOrDefault(s => s.Status == StepStatus.Failed || s.Status == StepStatus.Aborted)?.FailureReason
        };
    }

    private static StepValidationResult ValidateStepPreconditions(ShellContext context, TaskStep step, ExecutionPlan plan)
    {
        // 1. Elevation check
        if (step.RequiresElevation && !context.IsElevated)
        {
            return new StepValidationResult(false, $"Step {step.StepIndex + 1} requires Administrator elevation, but current session is not elevated.");
        }

        // 2. Empty script check
        if (string.IsNullOrWhiteSpace(step.Translation.PowerShellScript))
        {
            return new StepValidationResult(false, $"Step {step.StepIndex + 1} could not be translated into a valid operation.");
        }

        return new StepValidationResult(true);
    }

    private static void SkipRemainingSteps(ExecutionPlan plan, int startIndex, string reason)
    {
        for (int j = startIndex; j < plan.Steps.Count; j++)
        {
            plan.Steps[j].Status = StepStatus.Skipped;
            plan.Steps[j].FailureReason = reason;
        }
    }
}
