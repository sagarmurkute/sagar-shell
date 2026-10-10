using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Execution;

/// <summary>
/// Provides zero-process-overhead native .NET and Win32 direct execution for common read queries.
/// </summary>
public static class NativeWindowsExecutor
{
    public static ExecutionResult ListFilesFast(string directoryPath)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var dir = new DirectoryInfo(directoryPath);
            if (!dir.Exists)
            {
                return ExecutionResult.CreateFailure($"Directory '{directoryPath}' does not exist.", 1, sw.Elapsed);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Mode                 LastWriteTime         Length Name");
            sb.AppendLine($"----                 -------------         ------ ----");

            foreach (var d in dir.EnumerateDirectories())
            {
                sb.AppendLine($"d----       {d.LastWriteTime:yyyy-MM-dd HH:mm:ss}                {d.Name}");
            }
            foreach (var f in dir.EnumerateFiles())
            {
                sb.AppendLine($"-a---       {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}   {f.Length,11} {f.Name}");
            }

            sw.Stop();
            return ExecutionResult.CreateSuccess(sb.ToString().TrimEnd(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.CreateFailure(ex.Message, 1, sw.Elapsed);
        }
    }

    public static ExecutionResult ListProcessesFast()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var procs = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .Take(40);

            var sb = new StringBuilder();
            sb.AppendLine($"{"Id",-8} {"ProcessName",-30} {"Memory (MB)",12}");
            sb.AppendLine(new string('-', 52));

            foreach (var p in procs)
            {
                try
                {
                    var memMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 2);
                    sb.AppendLine($"{p.Id,-8} {p.ProcessName,-30} {memMb,12:F2}");
                }
                catch
                {
                    // Ignore processes where access is denied
                }
            }

            sw.Stop();
            return ExecutionResult.CreateSuccess(sb.ToString().TrimEnd(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.CreateFailure(ex.Message, 1, sw.Elapsed);
        }
    }

    public static ExecutionResult GetDrivesFast()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
            var sb = new StringBuilder();
            sb.AppendLine($"{"Drive",-8} {"Format",-8} {"Total GB",10} {"Free GB",10} {"% Free",8}");
            sb.AppendLine(new string('-', 48));

            foreach (var d in drives)
            {
                var totalGb = Math.Round(d.TotalSize / (1024.0 * 1024.0 * 1024.0), 2);
                var freeGb = Math.Round(d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
                var pctFree = Math.Round((d.TotalFreeSpace / (double)d.TotalSize) * 100.0, 1);
                sb.AppendLine($"{d.Name,-8} {d.DriveFormat,-8} {totalGb,10:F2} {freeGb,10:F2} {pctFree,7:F1}%");
            }

            sw.Stop();
            return ExecutionResult.CreateSuccess(sb.ToString().TrimEnd(), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ExecutionResult.CreateFailure(ex.Message, 1, sw.Elapsed);
        }
    }
}
