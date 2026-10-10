using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence;

/// <summary>
/// Inspects generated operations to determine safety, risk classification, admin requirements, and preview safeguards.
/// </summary>
public static class SafetyAnalyzer
{
    private static readonly Regex CriticalCommandsRegex = new(
        @"\b(?:Remove-Item\s+.*-(?:Recurse|r)\b|Clear-RecycleBin|Format-Volume|Initialize-Disk|Clear-Disk|Stop-Computer|Restart-Computer|shutdown\s+/[sr]|Remove-ItemProperty\s+.*(?:HKLM|HKCU)|Set-ExecutionPolicy\s+(?:Bypass|Unrestricted)|rmdir\s+/[sq]|del\s+/[sqf])\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HighRiskCommandsRegex = new(
        @"\b(?:Stop-Process|kill|Stop-Service|Restart-Service|Remove-Item|del|rm|Remove-AppxPackage|git\s+reset\s+--hard|git\s+clean\s+-(?:f|fd|fdx)|Move-Item|Rename-Item|Set-NetFirewallRule|netsh|New-NetFirewallRule)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModerateRiskCommandsRegex = new(
        @"\b(?:New-Item|mkdir|md|Copy-Item|cp|Set-Content|Add-Content|Out-File|Start-Process|Start-Service|Set-ItemProperty|git\s+commit|git\s+push|git\s+stash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ElevationRequiredRegex = new(
        @"\b(?:Stop-Service|Start-Service|Restart-Service|Set-Service|Set-ExecutionPolicy|Format-Volume|Initialize-Disk|Clear-Disk|Set-NetIPAddress|New-NetFirewallRule|Set-NetFirewallRule|netsh|DISM|sfc|Get-BitLockerVolume|Enable-WindowsOptionalFeature)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DangerousPathTargetsRegex = new(
        @"(?:C:\\(?:Windows|System32|Program Files|Program Files \(x86\)|Users)?\s*$|C:\\$|\$env:SystemRoot|\$env:ProgramFiles)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RiskAnalysisResult Analyze(string powerShellScript, IntentCategory category = IntentCategory.GeneralQuery)
    {
        if (string.IsNullOrWhiteSpace(powerShellScript))
        {
            return new RiskAnalysisResult(RiskLevel.Safe, false, "Empty or non-executable query.");
        }

        bool requiresElevation = ElevationRequiredRegex.IsMatch(powerShellScript);

        // Check for critical root path deletion
        if (powerShellScript.Contains("Remove-Item", StringComparison.OrdinalIgnoreCase) && DangerousPathTargetsRegex.IsMatch(powerShellScript))
        {
            return new RiskAnalysisResult(
                RiskLevel.Critical,
                true,
                "CRITICAL WARNING: This command attempts to delete or alter a core Windows system directory.",
                suggestedWhatIf: AddWhatIf(powerShellScript)
            );
        }

        if (CriticalCommandsRegex.IsMatch(powerShellScript))
        {
            return new RiskAnalysisResult(
                RiskLevel.Critical,
                requiresElevation,
                "High-impact or irreversible action (e.g. recursive delete, system shutdown, or disk operation).",
                suggestedWhatIf: AddWhatIf(powerShellScript)
            );
        }

        if (HighRiskCommandsRegex.IsMatch(powerShellScript))
        {
            return new RiskAnalysisResult(
                RiskLevel.High,
                requiresElevation,
                "Modifies existing resources, terminates running processes, or stops services.",
                suggestedWhatIf: AddWhatIf(powerShellScript)
            );
        }

        if (ModerateRiskCommandsRegex.IsMatch(powerShellScript))
        {
            return new RiskAnalysisResult(
                RiskLevel.Moderate,
                requiresElevation,
                "Creates new items, copies data, or starts applications.",
                suggestedWhatIf: AddWhatIf(powerShellScript)
            );
        }

        if (category == IntentCategory.Navigation)
        {
            return new RiskAnalysisResult(RiskLevel.Low, false, "Changes current working directory.");
        }

        return new RiskAnalysisResult(RiskLevel.Safe, requiresElevation, "Safe read-only or inspection query.");
    }

    public static string AddWhatIf(string powerShellScript)
    {
        if (powerShellScript.Contains("-WhatIf", StringComparison.OrdinalIgnoreCase))
            return powerShellScript;

        if (powerShellScript.Contains("Remove-Item", StringComparison.OrdinalIgnoreCase))
        {
            return powerShellScript + " -WhatIf";
        }
        if (powerShellScript.Contains("Stop-Process", StringComparison.OrdinalIgnoreCase))
        {
            return powerShellScript + " -WhatIf";
        }
        if (powerShellScript.Contains("Stop-Service", StringComparison.OrdinalIgnoreCase))
        {
            return powerShellScript + " -WhatIf";
        }
        return powerShellScript;
    }
}

public record RiskAnalysisResult(RiskLevel Level, bool RequiresElevation, string Explanation, string? suggestedWhatIf = null);
