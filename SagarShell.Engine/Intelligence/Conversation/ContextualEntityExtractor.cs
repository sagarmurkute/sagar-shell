using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Conversation;

/// <summary>
/// Extracts and resolves contextual entities (e.g. process with most CPU, largest file, blocked port)
/// from in-memory structured output objects and prior conversational turns.
/// </summary>
public static class ContextualEntityExtractor
{
    public record ExtractedProcessTarget(int ProcessId, string ProcessName, double Cpu, double RamMb);
    public record ExtractedFileTarget(string FilePath, string FileName, long SizeBytes);

    /// <summary>
    /// Finds the process consuming the most CPU from recent command outputs or memory.
    /// </summary>
    public static ExtractedProcessTarget? FindProcessWithMostCpu(ShellContext context)
    {
        if (context.LastOutputObjects != null && context.LastOutputObjects.Count > 0)
        {
            var processCandidates = new List<ExtractedProcessTarget>();

            foreach (var obj in context.LastOutputObjects)
            {
                if (obj == null) continue;

                int pid = 0;
                string name = "";
                double cpu = 0;
                double ramMb = 0;

                if (obj.Properties["Id"]?.Value is int idVal) pid = idVal;
                else if (int.TryParse(obj.Properties["Id"]?.Value?.ToString(), out int parsedId)) pid = parsedId;

                name = obj.Properties["ProcessName"]?.Value?.ToString()
                    ?? obj.Properties["Name"]?.Value?.ToString()
                    ?? "Process";

                if (obj.Properties["CPU"]?.Value is double cpuVal) cpu = cpuVal;
                else if (double.TryParse(obj.Properties["CPU"]?.Value?.ToString(), out double parsedCpu)) cpu = parsedCpu;

                if (obj.Properties["WorkingSet64"]?.Value is long wsVal) ramMb = Math.Round(wsVal / (1024.0 * 1024.0), 1);
                else if (obj.Properties["RAM_MB"]?.Value is double ramVal) ramMb = ramVal;

                if (pid > 0 || !string.IsNullOrWhiteSpace(name))
                {
                    processCandidates.Add(new ExtractedProcessTarget(pid, name, cpu, ramMb));
                }
            }

            if (processCandidates.Count > 0)
            {
                return processCandidates.OrderByDescending(p => p.Cpu).First();
            }
        }

        // Fallback: If context remembers a process name
        if (context.Memory.LastReferencedProcessNames.Count > 0)
        {
            var lastName = context.Memory.LastReferencedProcessNames[^1];
            var lastPid = context.Memory.LastReferencedProcessIds.Count > 0 ? context.Memory.LastReferencedProcessIds[^1] : 0;
            return new ExtractedProcessTarget(lastPid, lastName, 0, 0);
        }

        return null;
    }

    /// <summary>
    /// Finds the process consuming the most memory/RAM from recent command outputs.
    /// </summary>
    public static ExtractedProcessTarget? FindProcessWithMostMemory(ShellContext context)
    {
        if (context.LastOutputObjects != null && context.LastOutputObjects.Count > 0)
        {
            var processCandidates = new List<ExtractedProcessTarget>();

            foreach (var obj in context.LastOutputObjects)
            {
                if (obj == null) continue;

                int pid = 0;
                string name = "";
                double cpu = 0;
                double ramMb = 0;

                if (obj.Properties["Id"]?.Value is int idVal) pid = idVal;
                else if (int.TryParse(obj.Properties["Id"]?.Value?.ToString(), out int parsedId)) pid = parsedId;

                name = obj.Properties["ProcessName"]?.Value?.ToString()
                    ?? obj.Properties["Name"]?.Value?.ToString()
                    ?? "Process";

                if (obj.Properties["WorkingSet64"]?.Value is long wsVal) ramMb = Math.Round(wsVal / (1024.0 * 1024.0), 1);
                else if (obj.Properties["RAM_MB"]?.Value is double ramVal) ramMb = ramVal;

                if (pid > 0 || !string.IsNullOrWhiteSpace(name))
                {
                    processCandidates.Add(new ExtractedProcessTarget(pid, name, cpu, ramMb));
                }
            }

            if (processCandidates.Count > 0)
            {
                return processCandidates.OrderByDescending(p => p.RamMb).First();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the largest file from recent file listing output.
    /// </summary>
    public static ExtractedFileTarget? FindLargestFile(ShellContext context)
    {
        if (context.LastOutputObjects != null && context.LastOutputObjects.Count > 0)
        {
            var fileCandidates = new List<ExtractedFileTarget>();

            foreach (var obj in context.LastOutputObjects)
            {
                if (obj == null) continue;

                string fullName = obj.Properties["FullName"]?.Value?.ToString() ?? "";
                string name = obj.Properties["Name"]?.Value?.ToString() ?? fullName;
                long length = 0;

                if (obj.Properties["Length"]?.Value is long lenVal) length = lenVal;
                else if (long.TryParse(obj.Properties["Length"]?.Value?.ToString(), out long parsedLen)) length = parsedLen;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    fileCandidates.Add(new ExtractedFileTarget(fullName, name, length));
                }
            }

            if (fileCandidates.Count > 0)
            {
                return fileCandidates.OrderByDescending(f => f.SizeBytes).First();
            }
        }

        return null;
    }
}
