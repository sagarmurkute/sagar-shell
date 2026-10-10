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
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Exiting Sagar Shell. Goodbye!");
                Console.ResetColor();
                break;
            }

            if (trimmed.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                PrintHistory(context);
                continue;
            }

            if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
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

            // Perform translation
            var request = new TranslationRequest
            {
                NaturalLanguageInput = trimmed,
                Context = context,
                EnableDryRun = isDryRun
            };

            var translation = await engine.TranslateAsync(request);

            // Display translation breakdown
            PrintTranslationSummary(translation);

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

            if (!result.Success && !string.IsNullOrEmpty(result.StandardError))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] {result.StandardError}");
                Console.ResetColor();
            }

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

    private static void PrintHistory(ShellContext context)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Command History ---");
        Console.ResetColor();

        if (context.History.Count == 0)
        {
            Console.WriteLine("No commands recorded yet in this session.");
            return;
        }

        for (int i = 0; i < context.History.Count; i++)
        {
            var h = context.History[i];
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{i + 1}] {h.Timestamp:HH:mm:ss} ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"\"{h.Input}\"");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" -> ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(h.Translation.PowerShellScript);
        }
        Console.ResetColor();
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Sagar Shell Engine Help & Example Phrases ---");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("File System:");
        Console.WriteLine("  • list files in downloads");
        Console.WriteLine("  • find all pdf files");
        Console.WriteLine("  • show top 5 largest files in current folder");
        Console.WriteLine("  • create a folder called Projects");
        Console.WriteLine("  • create file notes.txt with content 'Hello World'");
        Console.WriteLine("  • search for 'TODO' in all .cs files");
        Console.WriteLine("  • read file package.json / show first 20 lines of app.log");
        Console.WriteLine("  • zip folder src to backup.zip / empty recycle bin");

        Console.WriteLine("\nProcess & Port Management:");
        Console.WriteLine("  • show running processes / what is using the most cpu");
        Console.WriteLine("  • show top 5 memory hogs / find process chrome");
        Console.WriteLine("  • kill process chrome / kill pid 1234");
        Console.WriteLine("  • what is using port 3000 / kill whatever is using port 3000");

        Console.WriteLine("\nFollow-ups & Pronoun Intelligence:");
        Console.WriteLine("  • Type 'list all pdf files', then 'sort them by size', then 'show first 3'");
        Console.WriteLine("  • Type 'what is using port 8080', then 'kill it'");

        Console.WriteLine("\nNetworking & System Info:");
        Console.WriteLine("  • what is my ip / what is my public ip / ping google.com");
        Console.WriteLine("  • show open ports / flush dns / show saved wifi passwords");
        Console.WriteLine("  • show system info / free ram / cpu usage / disk space / uptime");

        Console.WriteLine("\nGit & Navigation:");
        Console.WriteLine("  • git status / commit everything with message 'update' / show last 5 commits");
        Console.WriteLine("  • go to desktop / go back / cd .. / clear screen");
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
