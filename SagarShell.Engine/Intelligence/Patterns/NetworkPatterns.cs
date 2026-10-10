using System;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles networking intents: IP address discovery, ping, port testing, DNS flushing, Wi-Fi passwords, and active connections.
/// </summary>
public class NetworkPatterns : ICommandPattern
{
    public int Priority => 80;

    // IP address queries: "what is my ip", "show local ip", "what is my public ip", "ipconfig"
    private static readonly Regex IpQueryRegex = new(
        @"\b(?:what\s+is\s+my\s+(?:local\s+|public\s+|external\s+)?ip|my\s+ip\s+address|show\s+(?:my\s+)?(?:local\s+|public\s+|external\s+)?ip|ip\s+address|ipconfig|get\s+ip|get\s+my\s+ip)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Ping: "ping google.com", "ping 8.8.8.8 5 times", "test ping to cloudflare.com"
    private static readonly Regex PingRegex = new(
        @"^(?:test\s+)?ping(?:\s+to)?\s+['""]?(?<host>[a-zA-Z0-9_\-\.]+)(?:['""]?)?(?:\s+(?<count>\d+)\s+times?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Port testing: "test if port 80 is open on example.com", "test port 443 on google.com"
    private static readonly Regex PortTestRegex = new(
        @"(?:test|check)\s+(?:if\s+)?port\s+(?<port>\d+)(?:\s+is\s+open)?\s+(?:on|at)\s+['""]?(?<host>[a-zA-Z0-9_\-\.]+)['""]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // DNS Flush: "flush dns", "clear dns cache", "flush dns cache"
    private static readonly Regex DnsFlushRegex = new(
        @"\b(?:flush|clear|reset)\s+(?:the\s+)?dns(?:\s+cache)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Open ports / active connections: "show open ports", "show listening ports", "show network connections", "netstat"
    private static readonly Regex OpenPortsRegex = new(
        @"\b(?:open\s+ports|listening\s+ports|active\s+connections|netstat|network\s+connections)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Wi-Fi: "show wifi networks", "show wifi password for HomeNetwork", "get wifi password"
    private static readonly Regex WifiPasswordRegex = new(
        @"(?:wifi|wi-fi)\s+password(?:\s+for\s+['""]?(?<ssid>[^'""]+)['""]?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WifiNetworksRegex = new(
        @"\b(?:show|list|scan|find)\s+(?:available\s+)?(?:wifi|wi-fi)\s+networks\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "ip" or "ping" or "netstat"
            || IpQueryRegex.IsMatch(input)
            || PingRegex.IsMatch(input)
            || PortTestRegex.IsMatch(input)
            || DnsFlushRegex.IsMatch(input)
            || OpenPortsRegex.IsMatch(input)
            || WifiPasswordRegex.IsMatch(input)
            || WifiNetworksRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. DNS Flush
        if (DnsFlushRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Clear-DnsClientCache; Write-Host \"DNS Client Cache successfully flushed.\"",
                "Flushes and resets the Windows DNS resolver cache.",
                RiskLevel.Moderate,
                IntentCategory.Network
            );
        }

        // 2. Wi-Fi Password
        var wifiPassMatch = WifiPasswordRegex.Match(input);
        if (wifiPassMatch.Success)
        {
            var ssid = wifiPassMatch.Groups["ssid"].Value.Trim();
            if (!string.IsNullOrEmpty(ssid))
            {
                var ps = $"netsh wlan show profile name=\"{ssid}\" key=clear";
                var result = TranslationResult.CreateSuccess(
                    input,
                    ps,
                    $"Retrieves the saved Wi-Fi security key for SSID '{ssid}'.",
                    RiskLevel.Safe,
                    IntentCategory.Network
                );
                result.ExtractedEntities!.TargetUrlOrHost = ssid;
                return result;
            }
            else
            {
                var ps = "(netsh wlan show profiles) | Select-String ':\\s+(.+)$' | ForEach-Object { $name = $_.Matches[0].Groups[1].Value.Trim(); [PSCustomObject]@{ Profile = $name; Key = ((netsh wlan show profile name=\"$name\" key=clear) | Select-String 'Key Content\\s+:\\s+(.+)$').Matches[0].Groups[1].Value } }";
                return TranslationResult.CreateSuccess(
                    input,
                    ps,
                    "Displays all saved Wi-Fi profiles and their plaintext passwords.",
                    RiskLevel.Safe,
                    IntentCategory.Network
                );
            }
        }

        // 3. Wi-Fi Networks
        if (WifiNetworksRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "netsh wlan show networks mode=bssid",
                "Scans and lists currently detectable Wi-Fi wireless networks.",
                RiskLevel.Safe,
                IntentCategory.Network
            );
        }

        // 4. Port Test
        var portTestMatch = PortTestRegex.Match(input);
        if (portTestMatch.Success)
        {
            var host = portTestMatch.Groups["host"].Value.Trim();
            var port = portTestMatch.Groups["port"].Value.Trim();
            var ps = $"Test-NetConnection -ComputerName '{host}' -Port {port} | Select-Object ComputerName, RemotePort, TcpTestSucceeded, PingSucceeded";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Tests TCP socket reachability for port {port} on '{host}'.",
                RiskLevel.Safe,
                IntentCategory.Network
            );
            result.ExtractedEntities!.TargetUrlOrHost = host;
            if (int.TryParse(port, out var p)) result.ExtractedEntities.Ports.Add(p);
            return result;
        }

        // 5. Ping
        var pingMatch = PingRegex.Match(input);
        if (pingMatch.Success)
        {
            var host = pingMatch.Groups["host"].Value.Trim();
            var countStr = pingMatch.Groups["count"].Value;
            var count = int.TryParse(countStr, out var c) && c > 0 ? c : 4;
            var ps = $"Test-Connection -TargetName '{host}' -Count {count}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Pings '{host}' {count} times.",
                RiskLevel.Safe,
                IntentCategory.Network
            );
            result.ExtractedEntities!.TargetUrlOrHost = host;
            result.SuggestedFollowUps.Add($"test if port 80 is open on {host}");
            result.SuggestedFollowUps.Add($"test if port 443 is open on {host}");
            return result;
        }

        // 6. Open / Listening Ports & Connections
        if (OpenPortsRegex.IsMatch(input))
        {
            return TranslationResult.CreateSuccess(
                input,
                "Get-NetTCPConnection -State Listen | Select-Object LocalAddress, LocalPort, OwningProcess, @{{Name='ProcessName'; Expression={{(Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName}}}} | Sort-Object LocalPort",
                "Lists all currently listening TCP network ports and associated process owners.",
                RiskLevel.Safe,
                IntentCategory.Network
            );
        }

        // 7. IP Address & Adapters
        if (lower.Contains("public") || lower.Contains("external"))
        {
            return TranslationResult.CreateSuccess(
                input,
                "(Invoke-RestMethod -Uri 'https://api.ipify.org?format=json').ip",
                "Fetches external/public facing WAN IP address.",
                RiskLevel.Safe,
                IntentCategory.Network
            );
        }

        return TranslationResult.CreateSuccess(
            input,
            "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -notlike '*Loopback*' } | Select-Object InterfaceAlias, IPAddress, IPv4Address, PrefixLength",
            "Retrieves local IPv4 network adapter configurations.",
            RiskLevel.Safe,
            IntentCategory.Network
        );
    }
}
