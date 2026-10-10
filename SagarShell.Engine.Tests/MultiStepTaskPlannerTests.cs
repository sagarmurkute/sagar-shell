using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using SagarShell.Engine.Planning;
using Xunit;

namespace SagarShell.Engine.Tests;

public class MultiStepTaskPlannerTests
{
    private readonly NaturalLanguageEngine _engine;
    private readonly MultiStepTaskPlanner _planner;

    public MultiStepTaskPlannerTests()
    {
        _engine = new NaturalLanguageEngine();
        _planner = new MultiStepTaskPlanner(_engine);
    }

    [Theory]
    [InlineData("Find old videos in Downloads, move them to Archive, then compress them", true)]
    [InlineData("create folder Backup and copy *.log to Backup then zip Backup", true)]
    [InlineData("make directory temp; touch temp\\test.txt; show files in temp", true)]
    [InlineData("find all .pdf files after that sort them by size", true)]
    [InlineData("list files in downloads", false)]
    [InlineData("show running processes", false)]
    [InlineData("what is my ip address", false)]
    public void IsCompoundInstruction_CorrectlyIdentifiesMultiStep(string input, bool expectedCompound)
    {
        var result = _planner.IsCompoundInstruction(input);
        Assert.Equal(expectedCompound, result);
    }

    [Fact]
    public void Splitter_DecomposesSequentialConnectors()
    {
        var input = "Find old videos in Downloads, move them to Archive, then compress them";
        var parts = CompoundInstructionSplitter.Split(input);

        Assert.Equal(3, parts.Count);
        Assert.Equal("Find old videos in Downloads", parts[0]);
        Assert.Equal("move them to Archive", parts[1]);
        Assert.Equal("compress them", parts[2]);
    }

    [Fact]
    public void Splitter_DecomposesActionAndConjunctions()
    {
        var input = "create folder Backup and copy *.log to Backup then zip Backup";
        var parts = CompoundInstructionSplitter.Split(input);

        Assert.Equal(3, parts.Count);
        Assert.Contains("Backup", parts[0]);
        Assert.Contains("copy", parts[1]);
        Assert.Contains("zip", parts[2]);
    }

    [Fact]
    public void CreatePlan_BuildsOrderedDependencyGraph()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var input = "Find old videos in Downloads, move them to Archive, then compress them";

        var plan = _planner.CreatePlan(input, context);

        Assert.NotNull(plan);
        Assert.Equal(3, plan.TotalSteps);
        Assert.Equal(StepStatus.Pending, plan.Steps[0].Status);
        Assert.Equal(StepStatus.Pending, plan.Steps[1].Status);
        Assert.Equal(StepStatus.Pending, plan.Steps[2].Status);

        // Step 0 has no prerequisites
        Assert.Empty(plan.Steps[0].DependsOnStepIndices);

        // Step 1 depends on Step 0
        Assert.Single(plan.Steps[1].DependsOnStepIndices);
        Assert.Equal(0, plan.Steps[1].DependsOnStepIndices[0]);

        // Step 2 depends on Step 1
        Assert.Single(plan.Steps[2].DependsOnStepIndices);
        Assert.Equal(1, plan.Steps[2].DependsOnStepIndices[0]);

        // Overall risk reflects highest risk in steps (e.g. Move/Compress)
        Assert.True(plan.OverallRisk >= RiskLevel.Moderate);
    }

    [Fact]
    public void CreatePlan_FlowsSimulatedContextAcrossSteps()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var input = "Find old videos in Downloads, move them to Archive, then compress them";

        var plan = _planner.CreatePlan(input, context);

        Assert.Equal(3, plan.TotalSteps);
        // Step 1: lists files in Downloads
        Assert.Contains("Downloads", plan.Steps[0].Translation.PowerShellScript);

        // Step 2: "move them to Archive" resolves pronoun/context
        Assert.Contains("Archive", plan.Steps[1].Translation.PowerShellScript, StringComparison.OrdinalIgnoreCase);

        // Step 3: "compress them" targets Archive
        Assert.Contains("Compress-Archive", plan.Steps[2].Translation.PowerShellScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutePlanAsync_DenyingConfirmation_AbortsSafely()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());

        var input = "create folder TestPlannerDeny and delete folder TestPlannerDeny";
        var plan = _planner.CreatePlan(input, context);

        // Confirmation handler denies Step 2 (delete)
        var report = await _planner.ExecutePlanAsync(
            plan,
            executor,
            context,
            confirmationHandler: step =>
            {
                // Deny Step 2
                if (step.StepIndex == 1) return Task.FromResult(false);
                return Task.FromResult(true);
            });

        Assert.False(report.Success);
        Assert.Equal(StepStatus.Succeeded, plan.Steps[0].Status);
        Assert.Equal(StepStatus.Aborted, plan.Steps[1].Status);
        Assert.Contains("denied confirmation", plan.Steps[1].FailureReason);

        // Clean up created folder
        var testDir = Path.Combine(Directory.GetCurrentDirectory(), "TestPlannerDeny");
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
    }

    [Fact]
    public async Task ExecutePlanAsync_StepFailure_StopsSafelyAndSkipsSubsequentSteps()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());

        // Step 1 will deliberately fail (file not found in strict mode), so Step 2 must be skipped
        var plan = new ExecutionPlan
        {
            OriginalInstruction = "Failing plan test"
        };

        var step1 = new TaskStep
        {
            StepIndex = 0,
            RawInstruction = "Read non_existent_file_xyz_123.txt",
            Translation = TranslationResult.CreateSuccess(
                "Read non_existent_file_xyz_123.txt",
                "Get-Content -Path 'non_existent_file_xyz_123.txt' -ErrorAction Stop",
                "Reads non-existent file.",
                RiskLevel.Safe,
                IntentCategory.FileSystem)
        };

        var step2 = new TaskStep
        {
            StepIndex = 1,
            RawInstruction = "delete non_existent_file_xyz_123.txt",
            DependsOnStepIndices = new System.Collections.Generic.List<int> { 0 },
            Translation = TranslationResult.CreateSuccess(
                "delete non_existent_file_xyz_123.txt",
                "Remove-Item -Path 'non_existent_file_xyz_123.txt' -Force",
                "Deletes file.",
                RiskLevel.Critical,
                IntentCategory.FileSystem)
        };

        plan.Steps.Add(step1);
        plan.Steps.Add(step2);

        var report = await _planner.ExecutePlanAsync(plan, executor, context);

        Assert.False(report.Success);
        Assert.Equal(StepStatus.Failed, plan.Steps[0].Status);
        Assert.Equal(StepStatus.Skipped, plan.Steps[1].Status);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(0, report.SucceededCount);
        Assert.Contains("Aborted because Step 1 failed", plan.Steps[1].FailureReason);
    }

    [Fact]
    public async Task ExecutePlanAsync_PreValidation_FailsNonElevatedStepSafely()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory())
        {
            IsElevated = false
        };
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());

        var plan = new ExecutionPlan
        {
            OriginalInstruction = "Flush DNS and restart service"
        };

        var trans1 = TranslationResult.CreateSuccess(
            "restart service Spooler",
            "Restart-Service -Name 'Spooler' -Force",
            "Restarts spooler.",
            RiskLevel.High,
            IntentCategory.WindowsServices);
        trans1.RequiresElevation = true;

        var step1 = new TaskStep
        {
            StepIndex = 0,
            RawInstruction = "restart service Spooler",
            Translation = trans1
        };
        step1.Validator = (ctx, st) =>
        {
            if (st.RequiresElevation && !ctx.IsElevated)
                return new StepValidationResult(false, "Requires Admin privileges.");
            return new StepValidationResult(true);
        };

        var step2 = new TaskStep
        {
            StepIndex = 1,
            RawInstruction = "show service Spooler",
            DependsOnStepIndices = new System.Collections.Generic.List<int> { 0 },
            Translation = TranslationResult.CreateSuccess(
                "show service Spooler",
                "Get-Service -Name 'Spooler'",
                "Shows service.",
                RiskLevel.Safe,
                IntentCategory.WindowsServices)
        };

        plan.Steps.Add(step1);
        plan.Steps.Add(step2);

        var report = await _planner.ExecutePlanAsync(plan, executor, context);

        Assert.False(report.Success);
        Assert.Equal(StepStatus.Failed, plan.Steps[0].Status);
        Assert.Contains("Requires Admin privileges", plan.Steps[0].FailureReason);
        Assert.Equal(StepStatus.Skipped, plan.Steps[1].Status);
    }

    [Fact]
    public async Task ExecutePlanAsync_EndToEndSuccessfulMultiStepExecution()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "TestPlannerEndToEnd");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

        var context = new ShellContext(Directory.GetCurrentDirectory());
        using var executor = new ShellRuntimeManager(Directory.GetCurrentDirectory());

        var compoundInput = "make directory TestPlannerEndToEnd and create file TestPlannerEndToEnd\\sample.txt with content 'MultiStepWorking'";
        var plan = _planner.CreatePlan(compoundInput, context);

        Assert.Equal(2, plan.TotalSteps);

        var report = await _planner.ExecutePlanAsync(
            plan,
            executor,
            context,
            confirmationHandler: _ => Task.FromResult(true));

        Assert.True(report.Success, $"Plan failed: {report.AbortReason}");
        Assert.Equal(2, report.SucceededCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(0, report.SkippedCount);

        var sampleFile = Path.Combine(tempDir, "sample.txt");
        Assert.True(File.Exists(sampleFile));
        Assert.Contains("MultiStepWorking", File.ReadAllText(sampleFile));

        // Clean up
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }

    [Fact]
    public void ExecutionPlan_ToString_FormatsCleanHumanReadableSummary()
    {
        var context = new ShellContext(Directory.GetCurrentDirectory());
        var input = "Find old videos in Downloads, move them to Archive, then compress them";
        var plan = _planner.CreatePlan(input, context);

        var summary = plan.ToString();
        Assert.Contains("Execution Plan for:", summary);
        Assert.Contains("[1]", summary);
        Assert.Contains("[2]", summary);
        Assert.Contains("[3]", summary);
        Assert.Contains("Depends on Step", summary);
    }
}
