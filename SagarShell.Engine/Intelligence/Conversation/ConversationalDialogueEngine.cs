using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Conversation;

/// <summary>
/// Master Multi-Turn Conversational Dialogue Reasoner (Pure C# / 0 external LLM dependencies).
/// Understands user problems, goals, follow-up questions, affirmations, corrections,
/// ordinals, and semantic intents across multiple turns before executing actions.
/// </summary>
public static class ConversationalDialogueEngine
{
    private static readonly Regex AffirmationRegex = new(
        @"^(?:yes|yep|yeah|sure|go\s+ahead|do|do\s+it|please\s+do|ok|okay|run|run\s+it|proceed|start\s+with\s+that|let'?s\s+do\s+it|y|yes\s+please)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RejectionRegex = new(
        @"^(?:no|nope|cancel|don'?t|never\s+mind|stop|abort|n|no\s+thanks)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SystemSlowRegex = new(
        @"\b(?:laptop|pc|computer|system|windows|everything|things|it)\s+(?:feels?|is|seems?|running|runs?)\s+(?:so\s+|very\s+)?(?:slow|laggy|lagging|sluggish|freezing|frozen|unresponsive|hanging)\b|\b(?:why\s+is\s+(?:my\s+)?(?:laptop|pc|computer|system|everything|it)\s+(?:so\s+)?slow)\b|\b(?:high\s+cpu|cpu\s+is\s+maxed|fan\s+is\s+loud)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DiskFullRegex = new(
        @"\b(?:running\s+out\s+of\s+(?:disk\s+)?space|disk\s+is\s+full|storage\s+is\s+full|drive\s+is\s+full|free\s+up\s+(?:some\s+)?space|low\s+disk\s+space)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NetworkSlowRegex = new(
        @"\b(?:internet\s+is\s+(?:down|slow)|can'?t\s+connect\s+to\s+internet|network\s+(?:is\s+down|problem)|wifi\s+not\s+working)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CloseMostCpuRegex = new(
        @"\b(?:close|kill|terminate|stop|end)\s+(?:the\s+)?(?:one|process|app)\s+(?:using|taking|consuming)\s+(?:the\s+)?most\s+cpu\b|\b(?:kill|terminate|stop)\s+(?:the\s+)?(?:highest\s+cpu|top\s+cpu|heaviest)\s+(?:process|hog)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CloseMostMemoryRegex = new(
        @"\b(?:close|kill|terminate|stop|end)\s+(?:the\s+)?(?:one|process|app)\s+(?:using|taking|consuming)\s+(?:the\s+)?most\s+(?:memory|ram)\b|\b(?:kill|terminate|stop)\s+(?:the\s+)?(?:highest\s+ram|memory\s+hog)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DeleteLargestFileRegex = new(
        @"\b(?:delete|remove|clean)\s+(?:the\s+)?(?:largest|biggest)\s+(?:one|file)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks if the user's input represents a conversational dialogue turn (problem, affirmation, follow-up, or correction).
    /// </summary>
    public static bool IsConversationalTurn(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var raw = input.Trim().ToLowerInvariant();
        var clean = NaturalTextSanitizer.Sanitize(input).Trim();

        // 0. Check if an active clarification request is waiting for user choice
        if (context.ActiveClarification != null)
            return true;

        // 1. Check if an active proposal is pending and user is answering
        if (context.ActiveProposal != null && (AffirmationRegex.IsMatch(raw) || AffirmationRegex.IsMatch(clean) || RejectionRegex.IsMatch(raw) || RejectionRegex.IsMatch(clean)))
            return true;

        // 2. Check corrections
        if (ContextualCorrectionHandler.IsCorrection(input, context))
            return true;

        // 3. Check follow-ups
        if (ContextualFollowUpHandler.CanHandle(input, context))
            return true;

        // 4. Check problem statements
        if (SystemSlowRegex.IsMatch(raw) || SystemSlowRegex.IsMatch(clean) ||
            DiskFullRegex.IsMatch(raw) || DiskFullRegex.IsMatch(clean) ||
            NetworkSlowRegex.IsMatch(raw) || NetworkSlowRegex.IsMatch(clean))
            return true;

        // 5. Check contextual follow-up requests
        if (CloseMostCpuRegex.IsMatch(raw) || CloseMostCpuRegex.IsMatch(clean) ||
            CloseMostMemoryRegex.IsMatch(raw) || CloseMostMemoryRegex.IsMatch(clean) ||
            DeleteLargestFileRegex.IsMatch(raw) || DeleteLargestFileRegex.IsMatch(clean))
            return true;

        return false;
    }

    /// <summary>
    /// Evaluates conversational dialogue state and produces a context-aware TranslationResult.
    /// </summary>
    public static TranslationResult? TryProcessConversation(string input, ShellContext context)
    {
        var raw = input.Trim().ToLowerInvariant();
        var clean = NaturalTextSanitizer.Sanitize(input).Trim();

        // 0. Handle Active Clarification Resolution
        if (context.ActiveClarification != null)
        {
            var clarResult = ClarificationEngine.ResolveClarification(input, context);
            if (clarResult != null)
                return clarResult;
        }

        // 1. Handle Affirmation / Rejection of Pending Proposal
        if (context.ActiveProposal != null)
        {
            if (AffirmationRegex.IsMatch(raw) || AffirmationRegex.IsMatch(clean))
            {
                var actionToRun = context.ActiveProposal;
                context.ActiveProposal = null; // Consume proposal

                actionToRun.ConversationalMessage = "Starting the checks and analyzing resource usage now...";
                actionToRun.IsConversationalResponse = true;
                return actionToRun;
            }

            if (RejectionRegex.IsMatch(raw) || RejectionRegex.IsMatch(clean))
            {
                context.ActiveProposal = null; // Clear proposal
                return TranslationResult.CreateConversational(
                    input,
                    "Understood. Proposal cancelled. What would you like to do instead?",
                    null,
                    IntentCategory.GeneralQuery
                );
            }
        }

        // 2. Handle Contextual User Corrections (e.g. "no, I meant port 5000", "not that one, kill chrome")
        if (ContextualCorrectionHandler.IsCorrection(input, context))
        {
            var correctionResult = ContextualCorrectionHandler.TryHandleCorrection(input, context);
            if (correctionResult != null)
                return correctionResult;
        }

        // 3. Handle Contextual Follow-Ups (e.g. "close the first one", "sort by memory instead", "export that to json")
        if (ContextualFollowUpHandler.CanHandle(input, context))
        {
            var followUpResult = ContextualFollowUpHandler.TryHandle(input, context);
            if (followUpResult != null)
                return followUpResult;
        }

        // 4. Problem Statement: Laptop / PC is slow
        if (SystemSlowRegex.IsMatch(raw) || SystemSlowRegex.IsMatch(clean))
        {
            var diagnosticScript = "Get-Process | Where-Object { $_.CPU -gt 0 } | Sort-Object CPU -Descending | Select-Object -First 10 Id, ProcessName, @{Name='CPU_Sec';Expression={[math]::Round($_.CPU,1)}}, @{Name='RAM_MB';Expression={[math]::Round($_.WorkingSet64/1MB,1)}}";
            
            var proposal = TranslationResult.CreateSuccess(
                input,
                diagnosticScript,
                "Checks top running processes by CPU and RAM usage to diagnose system slowdown.",
                RiskLevel.Safe,
                IntentCategory.ProcessManagement
            );

            context.ActiveGoal = "Diagnosing system slowdown";
            context.ActiveProposal = proposal;

            return TranslationResult.CreateConversational(
                input,
                "I can check running processes and resource usage (CPU & Memory). Want me to start with that?",
                proposal,
                IntentCategory.ProcessManagement
            );
        }

        // 5. Problem Statement: Running out of disk space
        if (DiskFullRegex.IsMatch(raw) || DiskFullRegex.IsMatch(clean))
        {
            var diagnosticScript = "Get-Volume | Where-Object { $_.DriveType -eq 'Fixed' } | Select-Object DriveLetter, FileSystemLabel, @{Name='Free_GB';Expression={[math]::Round($_.SizeRemaining/1GB,2)}}, @{Name='Total_GB';Expression={[math]::Round($_.Size/1GB,2)}}";
            
            var proposal = TranslationResult.CreateSuccess(
                input,
                diagnosticScript,
                "Inspects hard drive partitions and available disk storage.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );

            context.ActiveGoal = "Analyzing disk storage";
            context.ActiveProposal = proposal;

            return TranslationResult.CreateConversational(
                input,
                "I can inspect your hard drives to see available free space and find large directories. Should I analyze storage usage?",
                proposal,
                IntentCategory.FileSystem
            );
        }

        // 6. Problem Statement: Network / Internet issue
        if (NetworkSlowRegex.IsMatch(raw) || NetworkSlowRegex.IsMatch(clean))
        {
            var diagnosticScript = "Test-NetConnection -ComputerName 8.8.8.8 | Select-Object ComputerName, PingSucceeded, RoundTripTime";
            
            var proposal = TranslationResult.CreateSuccess(
                input,
                diagnosticScript,
                "Tests internet connectivity and network latency to Google DNS (8.8.8.8).",
                RiskLevel.Safe,
                IntentCategory.Network
            );

            context.ActiveGoal = "Diagnosing network connection";
            context.ActiveProposal = proposal;

            return TranslationResult.CreateConversational(
                input,
                "I can test your network connection and internet latency. Want me to run network diagnostics?",
                proposal,
                IntentCategory.Network
            );
        }

        // 7. Follow-Up: "Close the one using the most CPU"
        if (CloseMostCpuRegex.IsMatch(raw) || CloseMostCpuRegex.IsMatch(clean))
        {
            var target = ContextualEntityExtractor.FindProcessWithMostCpu(context);

            if (target != null && target.ProcessId > 0)
            {
                var script = $"Stop-Process -Id {target.ProcessId} -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Identifies '{target.ProcessName}' (PID {target.ProcessId}) consuming the most CPU ({target.Cpu:F1}s CPU time).",
                    RiskLevel.High,
                    IntentCategory.ProcessManagement
                );

                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Closing '{target.ProcessName}' (PID {target.ProcessId}, {target.Cpu:F1}s CPU time) will immediately terminate this application and any unsaved state.";
                res.ConversationalMessage = $"Identified '{target.ProcessName}' (PID {target.ProcessId}) as consuming the highest CPU. I will terminate it after confirmation.";
                res.IsConversationalResponse = true;

                // Update context memory
                context.Memory.LastReferencedProcessNames.Add(target.ProcessName);
                context.Memory.LastReferencedProcessIds.Add(target.ProcessId);

                return res;
            }
            else if (target != null && !string.IsNullOrWhiteSpace(target.ProcessName))
            {
                var script = $"Stop-Process -Name '{target.ProcessName}' -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Terminates '{target.ProcessName}' which was identified as consuming high CPU.",
                    RiskLevel.High,
                    IntentCategory.ProcessManagement
                );

                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Terminating process '{target.ProcessName}' will close all active instances.";
                res.IsConversationalResponse = true;
                return res;
            }
            else
            {
                var script = "Get-Process | Where-Object { $_.CPU -gt 0 } | Sort-Object CPU -Descending | Select-Object -First 1 | Stop-Process -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    "Dynamically finds the process with the highest CPU usage and terminates it.",
                    RiskLevel.Critical,
                    IntentCategory.ProcessManagement
                );
                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = "This will immediately stop the single highest CPU-consuming process on the system.";
                res.IsConversationalResponse = true;
                return res;
            }
        }

        // 8. Follow-Up: "Close the one using the most memory"
        if (CloseMostMemoryRegex.IsMatch(raw) || CloseMostMemoryRegex.IsMatch(clean))
        {
            var target = ContextualEntityExtractor.FindProcessWithMostMemory(context);

            if (target != null && target.ProcessId > 0)
            {
                var script = $"Stop-Process -Id {target.ProcessId} -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Identifies '{target.ProcessName}' (PID {target.ProcessId}) consuming the most RAM ({target.RamMb:F1} MB).",
                    RiskLevel.High,
                    IntentCategory.ProcessManagement
                );

                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Closing '{target.ProcessName}' (PID {target.ProcessId}, {target.RamMb:F1} MB RAM) will immediately terminate this application.";
                res.ConversationalMessage = $"Identified '{target.ProcessName}' (PID {target.ProcessId}) as consuming the most RAM ({target.RamMb:F1} MB). I will terminate it after confirmation.";
                res.IsConversationalResponse = true;
                return res;
            }
            else
            {
                var script = "Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 1 | Stop-Process -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    "Dynamically finds the process with highest RAM usage and terminates it.",
                    RiskLevel.Critical,
                    IntentCategory.ProcessManagement
                );
                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = "This will immediately terminate the heaviest memory process on the system.";
                res.IsConversationalResponse = true;
                return res;
            }
        }

        // 9. Follow-Up: "Delete the largest file"
        if (DeleteLargestFileRegex.IsMatch(raw) || DeleteLargestFileRegex.IsMatch(clean))
        {
            var target = ContextualEntityExtractor.FindLargestFile(context);

            if (target != null && !string.IsNullOrWhiteSpace(target.FilePath))
            {
                var sizeMb = Math.Round(target.SizeBytes / (1024.0 * 1024.0), 2);
                var script = $"Remove-Item -Path '{target.FilePath}' -Force";
                var res = TranslationResult.CreateSuccess(
                    input,
                    script,
                    $"Deletes largest file '{target.FileName}' ({sizeMb} MB).",
                    RiskLevel.High,
                    IntentCategory.FileSystem
                );

                res.RequiresExplicitConfirmation = true;
                res.ImpactExplanation = $"Permanently deletes '{target.FilePath}' ({sizeMb} MB). This file will not be moved to Recycle Bin.";
                res.IsConversationalResponse = true;
                return res;
            }
        }

        // 10. Semantic Intent Parsing (Zero-LLM semantic understanding of varied natural English)
        var parsedIntent = SemanticIntentParser.Parse(clean, context);
        if (parsedIntent != null && parsedIntent.TargetType != SemanticIntentParser.EntityType.Unknown)
        {
            var semanticResult = SemanticIntentParser.GenerateTranslation(parsedIntent, input, context);
            if (semanticResult != null)
            {
                return semanticResult;
            }
        }

        return null;
    }
}
