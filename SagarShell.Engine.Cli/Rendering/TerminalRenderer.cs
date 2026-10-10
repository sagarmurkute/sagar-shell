using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using SagarShell.Engine.Diagnosis;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using SagarShell.Engine.Planning;
using Spectre.Console;

namespace SagarShell.Engine.Cli.Rendering;

/// <summary>
/// Modern, interactive terminal renderer using Spectre.Console for Sagar Shell.
/// Provides rich tables, panels, badges, status spinners, and syntax-highlighted previews.
/// </summary>
public static class TerminalRenderer
{
    public static void RenderBanner(ShellContext context, ShellRuntimeManager runtime)
    {
        AnsiConsole.Write(
            new FigletText("Sagar Shell")
                .LeftJustified()
                .Color(Color.Cyan1));

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        var elevationBadge = context.IsElevated 
            ? "[bold white on red] ADMINISTRATOR [/]" 
            : "[bold white on darkgreen] STANDARD USER [/]";

        var runtimeBadge = runtime.IsRunspaceActive
            ? "[bold green]⚡ In-Process Runspace (0ms latency)[/]"
            : "[bold yellow]⏳ Process Isolation Fallback[/]";

        grid.AddRow(new Markup($"[dim]Runtime:[/] {runtimeBadge}"), new Markup($"[dim]Privileges:[/] {elevationBadge}"));
        grid.AddRow(new Markup($"[dim]Working Directory:[/] [cyan]{Markup.Escape(context.CurrentDirectory)}[/]"), new Markup("[dim]Type[/] [bold yellow]'what can you do'[/] [dim]for commands[/]"));

        var panel = new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.SteelBlue),
            Header = new PanelHeader("[bold cyan] Natural Language Windows Shell [/]")
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void RenderTranslationSummary(TranslationResult translation)
    {
        if (translation == null) return;

        var riskColor = translation.Risk switch
        {
            RiskLevel.Safe => Color.Green,
            RiskLevel.Moderate => Color.Yellow,
            RiskLevel.High => Color.DarkOrange,
            RiskLevel.Critical => Color.Red,
            _ => Color.White
        };

        var riskBadge = $"[{riskColor.ToMarkup()}]● {translation.Risk.ToString().ToUpperInvariant()}[/]";
        var categoryBadge = $"[steelblue]⟦ {translation.Category} ⟧[/]";
        var elevationBadge = translation.RequiresElevation ? " [bold red]▲ Requires Admin[/]" : "";

        var content = new Rows(
            new Markup($"[bold dim]Natural Intent:[/] [white]{Markup.Escape(translation.OriginalInput)}[/]"),
            new Markup($"[bold dim]Risk Level:[/] {riskBadge}  {categoryBadge}{elevationBadge}"),
            new Markup($"[bold dim]Explanation:[/] [italic]{Markup.Escape(translation.Explanation)}[/]"),
            new Text(""),
            new Panel(new Markup($"[bold cyan]{Markup.Escape(translation.PowerShellScript)}[/]"))
            {
                Header = new PanelHeader("[dim]PowerShell Execution[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.DarkSlateGray1)
            }
        );

        var panel = new Panel(content)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(riskColor),
            Header = new PanelHeader("[bold] Translation Plan [/]")
        };

        AnsiConsole.Write(panel);
    }

    public static void RenderPlanSummary(ExecutionPlan plan)
    {
        if (plan == null) return;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.SteelBlue)
            .Title("[bold cyan]📋 Multi-Step Execution Plan[/]");

        table.AddColumn(new TableColumn("[bold]Step[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Action[/]"));
        table.AddColumn(new TableColumn("[bold]PowerShell Script[/]"));
        table.AddColumn(new TableColumn("[bold]Risk[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Requires Confirmation?[/]").Centered());

        foreach (var step in plan.Steps)
        {
            var riskColor = step.Risk switch
            {
                RiskLevel.Safe => "green",
                RiskLevel.Moderate => "yellow",
                RiskLevel.High => "darkorange",
                RiskLevel.Critical => "red",
                _ => "white"
            };

            var confirmBadge = step.RequiresConfirmation ? "[bold red]YES[/]" : "[dim green]No[/]";

            table.AddRow(
                $"[bold cyan]#{step.StepIndex + 1}[/]",
                Markup.Escape(step.Description),
                $"[cyan]{Markup.Escape(step.Translation.PowerShellScript)}[/]",
                $"[{riskColor}]{step.Risk}[/]",
                confirmBadge
            );
        }

        AnsiConsole.Write(table);
    }

    public static void RenderClarification(ClarificationRequest request)
    {
        if (request == null) return;

        AnsiConsole.MarkupLine($"\n[bold cyan]🤔 {Markup.Escape(request.Question)}[/]\n");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1);

        table.AddColumn(new TableColumn("[bold]Option[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Choice[/]"));
        table.AddColumn(new TableColumn("[bold]Action Preview[/]"));
        table.AddColumn(new TableColumn("[bold]Risk[/]").Centered());

        foreach (var opt in request.Options)
        {
            var riskColor = opt.Risk switch
            {
                RiskLevel.Safe => "green",
                RiskLevel.Moderate => "yellow",
                RiskLevel.High => "darkorange",
                RiskLevel.Critical => "red",
                _ => "white"
            };

            table.AddRow(
                $"[bold yellow][[{opt.Index}]][/]",
                $"[bold white]{Markup.Escape(opt.Label)}[/]\n[dim]{Markup.Escape(opt.Description)}[/]",
                $"[cyan]{Markup.Escape(opt.PowerShellScript)}[/]",
                $"[{riskColor}]{opt.Risk}[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    public static void RenderDiagnosis(ErrorDiagnosis diagnosis)
    {
        if (diagnosis == null) return;

        var panel = new Panel(new Rows(
            new Markup($"[bold red]❌ {Markup.Escape(diagnosis.PlainEnglishSummary)}[/]"),
            new Markup($"[bold dim]Category:[/] [yellow]{diagnosis.Category}[/]"),
            new Markup($"[bold dim]Likely Cause:[/] [white]{Markup.Escape(diagnosis.LikelyCause)}[/]"),
            new Text("")
        ))
        {
            Header = new PanelHeader("[bold red] Error Diagnosis & Root Cause [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red)
        };

        AnsiConsole.Write(panel);

        if (diagnosis.SuggestedFix != null)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Gold1)
                .Title("[bold yellow]🛠️ Suggested Auto-Recovery Fix[/]");

            table.AddColumn(new TableColumn("[bold]Remedy[/]"));
            table.AddColumn(new TableColumn("[bold]Action Command[/]"));
            table.AddColumn(new TableColumn("[bold]Admin Required?[/]").Centered());

            var rem = diagnosis.SuggestedFix;
            var adminBadge = rem.RequiresElevation ? "[bold red]YES[/]" : "[dim green]No[/]";

            table.AddRow(
                $"[white]{Markup.Escape(rem.Description)}[/]",
                $"[cyan]{Markup.Escape(rem.RemediationScript ?? "")}[/]",
                adminBadge
            );

            AnsiConsole.Write(table);
        }
    }

    public static void RenderStructuredPipelineResult(IReadOnlyList<object> objects, int maxRows = 25)
    {
        if (objects == null || objects.Count == 0)
            return;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.SteelBlue);

        // Extract properties from first object if PSObject or reflection
        var sample = objects[0];
        List<string> propertyNames = new();

        if (sample is PSObject psObj)
        {
            propertyNames = psObj.Properties.Select(p => p.Name).ToList();
        }
        else
        {
            propertyNames = sample.GetType().GetProperties().Select(p => p.Name).ToList();
        }

        if (propertyNames.Count == 0)
        {
            // Simple list of values
            table.AddColumn(new TableColumn("[bold cyan]Value[/]"));
            foreach (var item in objects.Take(maxRows))
            {
                table.AddRow(Markup.Escape(item?.ToString() ?? ""));
            }
        }
        else
        {
            // Limit columns to max 8 for readability in terminal
            var displayColumns = propertyNames.Take(8).ToList();
            foreach (var col in displayColumns)
            {
                table.AddColumn(new TableColumn($"[bold cyan]{Markup.Escape(col)}[/]"));
            }

            var rowsToShow = objects.Take(maxRows).ToList();
            foreach (var item in rowsToShow)
            {
                var rowValues = new List<string>();
                if (item is PSObject pso)
                {
                    foreach (var col in displayColumns)
                    {
                        var val = pso.Properties[col]?.Value?.ToString() ?? "";
                        rowValues.Add(Markup.Escape(val));
                    }
                }
                else
                {
                    var type = item.GetType();
                    foreach (var col in displayColumns)
                    {
                        var prop = type.GetProperty(col);
                        var val = prop?.GetValue(item)?.ToString() ?? "";
                        rowValues.Add(Markup.Escape(val));
                    }
                }
                table.AddRow(rowValues.ToArray());
            }
        }

        if (objects.Count > maxRows)
        {
            table.Caption = new TableTitle($"[dim]Showing top {maxRows} of {objects.Count} structured objects.[/]");
        }
        else
        {
            table.Caption = new TableTitle($"[dim]Total: {objects.Count} objects[/]");
        }

        AnsiConsole.Write(table);
    }

    public static void RenderHistory(IReadOnlyList<CommandHistoryEntry> records)
    {
        if (records == null || records.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No command history found in this session.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.SteelBlue)
            .Title("[bold cyan]📜 Sagar Shell Command History[/]");

        table.AddColumn(new TableColumn("[bold]#[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Time[/]"));
        table.AddColumn(new TableColumn("[bold]Natural Instruction[/]"));
        table.AddColumn(new TableColumn("[bold]PowerShell Script[/]"));
        table.AddColumn(new TableColumn("[bold]Risk[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

        int idx = 1;
        foreach (var item in records.TakeLast(20))
        {
            var isSuccess = item.Execution?.Success ?? true;
            var statusBadge = isSuccess ? "[green]✔ OK[/]" : "[red]✖ FAIL[/]";
            var risk = item.Translation?.Risk ?? RiskLevel.Safe;
            var riskColor = risk switch
            {
                RiskLevel.Safe => "green",
                RiskLevel.Moderate => "yellow",
                RiskLevel.High => "darkorange",
                RiskLevel.Critical => "red",
                _ => "white"
            };

            table.AddRow(
                $"[dim]{idx++}[/]",
                $"[dim]{item.Timestamp:HH:mm:ss}[/]",
                Markup.Escape(item.Input),
                $"[cyan]{Markup.Escape(item.Translation?.PowerShellScript ?? "")}[/]",
                $"[{riskColor}]{risk}[/]",
                statusBadge
            );
        }

        AnsiConsole.Write(table);
    }
}
