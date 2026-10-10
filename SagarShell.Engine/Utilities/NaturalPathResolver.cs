using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SagarShell.Engine.Utilities;

/// <summary>
/// Resolves natural English path descriptions into valid Windows / PowerShell paths.
/// </summary>
public static class NaturalPathResolver
{
    private static readonly Regex QuotedPathRegex = new(@"['""]([^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex DriveLetterRegex = new(@"\b([A-Za-z])\s*(?:drive|disk|root)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ResolvePath(string rawPath, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return ".";

        var trimmed = rawPath.Trim();

        // Handle quotes
        var match = QuotedPathRegex.Match(trimmed);
        if (match.Success)
        {
            trimmed = match.Groups[1].Value.Trim();
        }

        // Clean natural prefixes ("in the", "inside", "from the", "into the", "to", "folder", "directory")
        trimmed = Regex.Replace(trimmed, @"^(?:in\s+the|inside\s+(?:the\s+)?|from\s+(?:the\s+)?|into\s+(?:the\s+)?|to\s+(?:the\s+)?|at\s+(?:the\s+)?)\s+", "", RegexOptions.IgnoreCase).Trim();
        trimmed = Regex.Replace(trimmed, @"\s+(?:folder|directory|dir)$", "", RegexOptions.IgnoreCase).Trim();

        // Match drive shortcuts: "c drive", "d disk"
        var driveMatch = DriveLetterRegex.Match(trimmed);
        if (driveMatch.Success)
        {
            return $"{driveMatch.Groups[1].Value.ToUpper()}:\\";
        }

        // Check common aliases
        var lower = trimmed.ToLowerInvariant();
        return lower switch
        {
            "desktop" or "my desktop" or "the desktop" => "$HOME\\Desktop",
            "downloads" or "my downloads" or "the downloads" => "$HOME\\Downloads",
            "documents" or "my documents" or "docs" or "my docs" => "$HOME\\Documents",
            "pictures" or "my pictures" or "photos" or "my photos" => "$HOME\\Pictures",
            "videos" or "my videos" or "movies" => "$HOME\\Videos",
            "music" or "my music" => "$HOME\\Music",
            "home" or "home directory" or "user directory" or "user folder" or "~" => "$HOME",
            "temp" or "temporary" or "temp folder" => "$env:TEMP",
            "appdata" or "app data" => "$env:APPDATA",
            "localappdata" or "local app data" => "$env:LOCALAPPDATA",
            "program files" => "$env:ProgramFiles",
            "system32" or "windows system32" => "$env:SystemRoot\\System32",
            "root" or "c:" or "c:/" or "c:\\" => "C:\\",
            "here" or "this folder" or "this directory" or "current" or "current folder" or "current directory" or "." => ".",
            "parent" or "parent folder" or "parent directory" or "up" or "up one level" or ".." => "..",
            "up two" or "up 2" or "up two levels" => "..\\..",
            _ => NormalizeArbitraryPath(trimmed, baseDirectory)
        };
    }

    public static string EscapeForPowerShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "'.'";

        // If it starts with an environment variable or $HOME, don't enclose the variable in single quotes
        if (path.StartsWith("$HOME", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
        {
            // Use double quotes for PowerShell variable interpolation
            return $"\"{path.Replace("\"", "`\"")}\"";
        }

        if (path == "." || path == "..")
            return path;

        // Escape single quotes and wrap in single quotes
        return $"'{path.Replace("'", "''")}'";
    }

    private static string NormalizeArbitraryPath(string path, string? baseDirectory)
    {
        // Replace forward slashes with backslashes for Windows
        var normalized = path.Replace('/', '\\');

        // Handle ~/ paths
        if (normalized.StartsWith("~\\") || normalized == "~")
        {
            return "$HOME" + normalized[1..];
        }

        // Handle relative paths with baseDirectory if provided
        if (!string.IsNullOrWhiteSpace(baseDirectory) && !Path.IsPathRooted(normalized) && !normalized.StartsWith("$"))
        {
            // Return clean relative path or absolute if verified
            return normalized;
        }

        return normalized;
    }
}
