using System;
using System.IO;
using System.Text.RegularExpressions;
using SagarShell.Engine.Models;
using SagarShell.Engine.Utilities;

namespace SagarShell.Engine.Intelligence.Patterns;

/// <summary>
/// Handles all file system intents: listing, finding, creating, reading, searching (grep),
/// copying, moving, renaming, deleting, compressing, and calculating sizes.
/// </summary>
public class FileSystemPatterns : ICommandPattern
{
    public int Priority => 90;

    // List regex
    private static readonly Regex ListFilesRegex = new(
        @"^(?:list|show|get|display|view|see)\s+(?:all\s+)?(?:the\s+)?(?:files|items|contents|everything|directory|directories|folders)?(?:\s+(?:in|from|inside|at)\s+(?<path>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ListHiddenRegex = new(
        @"\b(?:hidden\s+files|show\s+hidden|all\s+files\s+including\s+hidden)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Extension search: "find all pdf files", "list all .json files in downloads", "find *.mp4 files"
    private static readonly Regex FindByExtensionRegex = new(
        @"^(?:find|list|show|search\s+for|get)\s+(?:all\s+)?(?:\*\.)?(?<ext>\.[a-zA-Z0-9]+|(?!files\b|the\b|all\b|hidden\b|empty\b|large\b|new\b|recent\b|any\b)[a-zA-Z0-9]+)\s+files?(?:\s+(?:in|inside|from|at)\s+(?<path>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Grep/Search content: "search for 'TODO' in all .cs files", "find 'apikey' in current folder"
    private static readonly Regex GrepContentRegex = new(
        @"^(?:search\s+for|find\s+text|find|grep)\s+['""](?<query>[^'""]+)['""](?:\s+in\s+(?:all\s+)?(?<filter>\*\.[a-zA-Z0-9]+|[a-zA-Z0-9*.]+\s+files?))?(?:\s+in\s+(?<path>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Create directory: "create folder Projects", "make a directory called test"
    private static readonly Regex CreateDirRegex = new(
        @"^(?:create|make|new)\s+(?:a\s+)?(?:new\s+)?(?:folder|directory|dir)(?:\s+(?:called|named))?\s+['""]?(?<name>[^'""]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Create file: "create file hello.txt with content 'world'", "touch test.js", "make a file test.txt"
    private static readonly Regex CreateFileRegex = new(
        @"^(?:create|make|touch|new)\s+(?:a\s+)?(?:new\s+)?(?:empty\s+)?file\s+['""]?(?<name>[^'""]+?)['""]?(?:\s+with\s+content\s+['""](?<content>.*)['""])?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Read file: "read file.txt", "view content of settings.json", "cat package.json", "show lines of test.log"
    private static readonly Regex ReadFileRegex = new(
        @"^(?:read|view|show|display|cat|open)\s+(?:the\s+)?(?:contents?\s+of\s+)?(?:file\s+)?['""]?(?<path>[^'""]+\.[a-zA-Z0-9_]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Head / Tail lines: "show first 20 lines of app.log", "tail 30 lines of error.log"
    private static readonly Regex HeadTailRegex = new(
        @"^(?:show\s+)?(?<type>first|top|last|head|tail)\s+(?<count>\d+)\s+lines\s+(?:of|from)\s+['""]?(?<path>[^'""]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Delete file / folder: "delete temp.txt", "remove folder bin", "delete all .log files"
    private static readonly Regex DeleteRegex = new(
        @"^(?:delete|remove|rm|erase|del)\s+(?:all\s+)?(?:the\s+)?(?:folder|directory|dir|file)?\s*['""]?(?<target>[^'""]+)['""]?(?:\s+recursively|\s+-r|\s+-f)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Copy / Move / Rename
    private static readonly Regex CopyRegex = new(
        @"^(?:copy|cp)\s+(?:file\s+|folder\s+)?['""]?(?<source>[^'""]+)['""]?\s+(?:to|into)\s+['""]?(?<dest>[^'""]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MoveRenameRegex = new(
        @"^(?:move|mv|rename)\s+(?:file\s+|folder\s+)?['""]?(?<source>[^'""]+)['""]?\s+to\s+['""]?(?<dest>[^'""]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Compress / Zip
    private static readonly Regex ZipRegex = new(
        @"^(?:zip|compress)\s+(?:folder\s+|directory\s+|file\s+)?['""]?(?<source>[^'""]+)['""]?\s+(?:to|into|as)\s+['""]?(?<dest>[^'""]+)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnzipRegex = new(
        @"^(?:unzip|extract|decompress)\s+['""]?(?<source>[^'""]+)['""]?(?:\s+(?:to|into)\s+['""]?(?<dest>[^'""]+)['""]?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Folder / File Size
    private static readonly Regex SizeCheckRegex = new(
        @"^(?:how\s+big\s+is|what\s+is\s+the\s+size\s+of|calculate\s+size\s+of|folder\s+size\s+of|get\s+size\s+of)\s+['""]?(?<path>.+?)['""]?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Empty recycle bin
    private static readonly Regex RecycleBinRegex = new(
        @"\b(?:empty|clean|clear)\s+(?:the\s+)?(?:recycle\s+bin|trash)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool CanHandle(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();
        return lower is "ls" or "dir" or "list files" or "show files"
            || ListFilesRegex.IsMatch(input)
            || FindByExtensionRegex.IsMatch(input)
            || GrepContentRegex.IsMatch(input)
            || CreateDirRegex.IsMatch(input)
            || CreateFileRegex.IsMatch(input)
            || ReadFileRegex.IsMatch(input)
            || HeadTailRegex.IsMatch(input)
            || DeleteRegex.IsMatch(input)
            || CopyRegex.IsMatch(input)
            || MoveRenameRegex.IsMatch(input)
            || ZipRegex.IsMatch(input)
            || UnzipRegex.IsMatch(input)
            || SizeCheckRegex.IsMatch(input)
            || RecycleBinRegex.IsMatch(input);
    }

    public TranslationResult Translate(string input, ShellContext? context)
    {
        var lower = input.ToLowerInvariant().Trim();

        // 1. Recycle Bin
        if (RecycleBinRegex.IsMatch(input))
        {
            var res = TranslationResult.CreateSuccess(
                input,
                "Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
                "Permanently empties the Windows Recycle Bin.",
                RiskLevel.Critical,
                IntentCategory.FileSystem
            );
            res.SuggestedFollowUps.Add("check free disk space");
            return res;
        }

        // 2. Head / Tail lines
        var headTailMatch = HeadTailRegex.Match(input);
        if (headTailMatch.Success)
        {
            var count = headTailMatch.Groups["count"].Value;
            var path = headTailMatch.Groups["path"].Value.Trim();
            var resolvedPath = NaturalPathResolver.ResolvePath(path);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolvedPath);
            var isTail = headTailMatch.Groups["type"].Value.ToLowerInvariant() is "last" or "tail";

            string ps = isTail
                ? $"Get-Content -Path {escaped} -Tail {count}"
                : $"Get-Content -Path {escaped} -TotalCount {count}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Displays the {(isTail ? "last" : "first")} {count} lines of {path}.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(path);
            return result;
        }

        // 3. Create Directory
        var createDirMatch = CreateDirRegex.Match(input);
        if (createDirMatch.Success)
        {
            var dirName = createDirMatch.Groups["name"].Value.Trim();
            var resolved = NaturalPathResolver.ResolvePath(dirName);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);
            var ps = $"New-Item -ItemType Directory -Path {escaped} -Force | Out-Null; Write-Host \"Created directory: {dirName}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Creates a new directory named '{dirName}'.",
                RiskLevel.Moderate,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(dirName);
            result.SuggestedFollowUps.Add($"cd into {dirName}");
            result.SuggestedFollowUps.Add($"list files in {dirName}");
            return result;
        }

        // 4. Create File
        var createFileMatch = CreateFileRegex.Match(input);
        if (createFileMatch.Success)
        {
            var fileName = createFileMatch.Groups["name"].Value.Trim();
            var content = createFileMatch.Groups["content"].Value;
            var resolved = NaturalPathResolver.ResolvePath(fileName);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);

            string ps;
            if (!string.IsNullOrEmpty(content))
            {
                var escapedContent = content.Replace("'", "''");
                ps = $"Set-Content -Path {escaped} -Value '{escapedContent}' -Encoding UTF8; Write-Host \"Created file '{fileName}' with content.\"";
            }
            else
            {
                ps = $"New-Item -ItemType File -Path {escaped} -Force | Out-Null; Write-Host \"Created file: {fileName}\"";
            }

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Creates file '{fileName}'" + (string.IsNullOrEmpty(content) ? "." : $" with initial text '{content}'."),
                RiskLevel.Moderate,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(fileName);
            result.SuggestedFollowUps.Add($"read {fileName}");
            return result;
        }

        // 5. Read File
        var readFileMatch = ReadFileRegex.Match(input);
        if (readFileMatch.Success)
        {
            var path = readFileMatch.Groups["path"].Value.Trim();
            var resolved = NaturalPathResolver.ResolvePath(path);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);
            var ps = $"Get-Content -Path {escaped}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Reads and outputs the content of file '{path}'.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(path);
            result.SuggestedFollowUps.Add($"count lines in {path}");
            result.SuggestedFollowUps.Add($"show first 10 lines of {path}");
            return result;
        }

        // 6. Grep / Search Content
        var grepMatch = GrepContentRegex.Match(input);
        if (grepMatch.Success)
        {
            var query = grepMatch.Groups["query"].Value.Trim();
            var filter = grepMatch.Groups["filter"].Value.Trim();
            var path = grepMatch.Groups["path"].Value.Trim();
            var resolvedPath = string.IsNullOrEmpty(path) ? "." : NaturalPathResolver.ResolvePath(path);
            var escapedPath = NaturalPathResolver.EscapeForPowerShell(resolvedPath);

            var filterArg = string.IsNullOrEmpty(filter) ? "*.*" : (filter.StartsWith("*.") ? filter : $"*.{filter.Replace("files", "").Trim()}");
            var escapedFilter = NaturalPathResolver.EscapeForPowerShell(filterArg);

            var ps = $"Get-ChildItem -Path {escapedPath} -Filter {escapedFilter} -Recurse -File -ErrorAction SilentlyContinue | Select-String -Pattern '{query.Replace("'", "''")}' | Select-Object Path, LineNumber, Line";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Searches for text '{query}' within {filterArg} files under {resolvedPath}.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.SearchTerm = query;
            return result;
        }

        // 7. Folder / File Size Check
        var sizeCheckMatch = SizeCheckRegex.Match(input);
        if (sizeCheckMatch.Success)
        {
            var path = sizeCheckMatch.Groups["path"].Value.Trim();
            var resolved = NaturalPathResolver.ResolvePath(path);
            var escaped = NaturalPathResolver.EscapeForPowerShell(resolved);
            var ps = $"Get-ChildItem -Path {escaped} -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum | Select-Object @{{Name='Path'; Expression={{{escaped}}}}}, @{{Name='SizeMB'; Expression={{[math]::Round($_.Sum / 1MB, 2)}}}}, @{{Name='SizeGB'; Expression={{[math]::Round($_.Sum / 1GB, 3)}}}}, @{{Name='FileCount'; Expression={{$_.Count}}}}";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Calculates total size and file count for '{path}'.",
                RiskLevel.Safe,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(path);
            return result;
        }

        // 8. Copy
        var copyMatch = CopyRegex.Match(input);
        if (copyMatch.Success)
        {
            var src = copyMatch.Groups["source"].Value.Trim();
            var dest = copyMatch.Groups["dest"].Value.Trim();
            var resSrc = NaturalPathResolver.ResolvePath(src);
            var resDest = NaturalPathResolver.ResolvePath(dest);
            var escSrc = NaturalPathResolver.EscapeForPowerShell(resSrc);
            var escDest = NaturalPathResolver.EscapeForPowerShell(resDest);

            var ps = $"Copy-Item -Path {escSrc} -Destination {escDest} -Recurse -Force; Write-Host \"Copied {src} to {dest}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Copies '{src}' to '{dest}'.",
                RiskLevel.Moderate,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(src);
            result.ExtractedEntities.Paths.Add(dest);
            return result;
        }

        // 9. Move / Rename
        var moveMatch = MoveRenameRegex.Match(input);
        if (moveMatch.Success)
        {
            var src = moveMatch.Groups["source"].Value.Trim();
            var dest = moveMatch.Groups["dest"].Value.Trim();
            var resSrc = NaturalPathResolver.ResolvePath(src);
            var resDest = NaturalPathResolver.ResolvePath(dest);
            var escSrc = NaturalPathResolver.EscapeForPowerShell(resSrc);
            var escDest = NaturalPathResolver.EscapeForPowerShell(resDest);

            var ps = $"Move-Item -Path {escSrc} -Destination {escDest} -Force; Write-Host \"Moved {src} to {dest}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Moves/renames '{src}' to '{dest}'.",
                RiskLevel.High,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(src);
            result.ExtractedEntities.Paths.Add(dest);
            return result;
        }

        // 10. Zip / Compress
        var zipMatch = ZipRegex.Match(input);
        if (zipMatch.Success)
        {
            var src = zipMatch.Groups["source"].Value.Trim();
            var dest = zipMatch.Groups["dest"].Value.Trim();
            if (!dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) dest += ".zip";
            var resSrc = NaturalPathResolver.ResolvePath(src);
            var resDest = NaturalPathResolver.ResolvePath(dest);
            var escSrc = NaturalPathResolver.EscapeForPowerShell(resSrc);
            var escDest = NaturalPathResolver.EscapeForPowerShell(resDest);

            var ps = $"Compress-Archive -Path {escSrc} -DestinationPath {escDest} -Force; Write-Host \"Compressed to {dest}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Zips '{src}' into '{dest}'.",
                RiskLevel.Moderate,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(dest);
            return result;
        }

        // 11. Unzip / Extract
        var unzipMatch = UnzipRegex.Match(input);
        if (unzipMatch.Success)
        {
            var src = unzipMatch.Groups["source"].Value.Trim();
            var dest = unzipMatch.Groups["dest"].Value.Trim();
            var resSrc = NaturalPathResolver.ResolvePath(src);
            var resDest = string.IsNullOrEmpty(dest) ? "." : NaturalPathResolver.ResolvePath(dest);
            var escSrc = NaturalPathResolver.EscapeForPowerShell(resSrc);
            var escDest = NaturalPathResolver.EscapeForPowerShell(resDest);

            var ps = $"Expand-Archive -Path {escSrc} -DestinationPath {escDest} -Force; Write-Host \"Extracted {src} into {resDest}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Extracts archive '{src}' into '{resDest}'.",
                RiskLevel.Moderate,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(src);
            return result;
        }

        // 12. Delete
        var deleteMatch = DeleteRegex.Match(input);
        if (deleteMatch.Success && !lower.StartsWith("list") && !lower.StartsWith("find"))
        {
            var target = deleteMatch.Groups["target"].Value.Trim();
            var resTarget = NaturalPathResolver.ResolvePath(target);
            var escTarget = NaturalPathResolver.EscapeForPowerShell(resTarget);

            var ps = $"Remove-Item -Path {escTarget} -Recurse -Force; Write-Host \"Deleted: {target}\"";

            var result = TranslationResult.CreateSuccess(
                input,
                ps,
                $"Deletes '{target}' (recursive & forced).",
                RiskLevel.Critical,
                IntentCategory.FileSystem
            );
            result.ExtractedEntities!.Paths.Add(target);
            return result;
        }

        // 13. Extension search or General List with Filters (size, date, hidden, top N)
        return BuildFilteredListResult(input);
    }

    private static TranslationResult BuildFilteredListResult(string input)
    {
        var lower = input.ToLowerInvariant();
        string targetPath = ".";
        string? filter = null;
        bool isHidden = ListHiddenRegex.IsMatch(input);

        // Path extraction
        var pathMatch = Regex.Match(input, @"\b(?:in|from|inside|at)\s+['""]?(?<path>[a-zA-Z0-9_\-\\\/.:~ ]+)['""]?$", RegexOptions.IgnoreCase);
        if (pathMatch.Success)
        {
            targetPath = NaturalPathResolver.ResolvePath(pathMatch.Groups["path"].Value.Trim());
        }

        // Extension check
        var extMatch = FindByExtensionRegex.Match(input);
        if (extMatch.Success)
        {
            var ext = extMatch.Groups["ext"].Value.Trim();
            if (!ext.StartsWith(".")) ext = "." + ext;
            filter = $"*{ext}";
        }
        else
        {
            // Check inline extensions like "show all .log files"
            var rawExtMatch = Regex.Match(input, @"\*\.[a-zA-Z0-9]+|\.[a-zA-Z0-9]+");
            if (rawExtMatch.Success && !rawExtMatch.Value.Contains(".."))
            {
                var val = rawExtMatch.Value;
                filter = val.StartsWith("*") ? val : $"*{val}";
            }
        }

        // Filters: size, date
        var sizeFilter = NaturalSizeParser.TryParse(input);
        var timeFilter = NaturalTimeParser.TryParse(input);

        // Build PowerShell command pipeline
        var escapedPath = NaturalPathResolver.EscapeForPowerShell(targetPath);
        var ps = $"Get-ChildItem -Path {escapedPath}";

        if (!string.IsNullOrEmpty(filter))
        {
            ps += $" -Filter '{filter}'";
        }

        if (isHidden)
        {
            ps += " -Force";
        }

        // Add size/time where-object clauses
        var whereClauses = new System.Collections.Generic.List<string>();
        if (sizeFilter != null)
        {
            whereClauses.Add(sizeFilter.PowerShellFilter);
        }
        if (timeFilter != null)
        {
            whereClauses.Add(timeFilter.PowerShellExpression);
        }

        if (whereClauses.Count > 0)
        {
            ps += $" | Where-Object {{ {string.Join(" -and ", whereClauses)} }}";
        }

        // Ordering & Limits: "largest 10 files", "top 5 largest"
        var isLargest = lower.Contains("largest") || lower.Contains("biggest");
        var isSmallest = lower.Contains("smallest");
        var isNewest = lower.Contains("newest") || lower.Contains("most recent");
        var isOldest = lower.Contains("oldest");

        if (isLargest)
        {
            ps += " | Sort-Object Length -Descending";
        }
        else if (isSmallest)
        {
            ps += " | Sort-Object Length";
        }
        else if (isNewest)
        {
            ps += " | Sort-Object LastWriteTime -Descending";
        }
        else if (isOldest)
        {
            ps += " | Sort-Object LastWriteTime";
        }

        var limitMatch = Regex.Match(input, @"\b(?:top|first)\s+(?<count>\d+)\b", RegexOptions.IgnoreCase);
        if (limitMatch.Success)
        {
            ps += $" | Select-Object -First {limitMatch.Groups["count"].Value}";
        }

        // Format nice output
        ps += " | Select-Object Name, Length, LastWriteTime, Extension";

        var result = TranslationResult.CreateSuccess(
            input,
            ps,
            $"Lists files in '{targetPath}'" + (filter != null ? $" matching '{filter}'" : "") + (sizeFilter != null ? $" with size {sizeFilter.ComparisonOperator} {sizeFilter.PowerShellUnit}" : "") + ".",
            RiskLevel.Safe,
            IntentCategory.FileSystem
        );

        result.ExtractedEntities!.Paths.Add(targetPath);
        if (filter != null) result.ExtractedEntities.FilterPattern = filter;

        result.SuggestedFollowUps.Add("sort them by size");
        result.SuggestedFollowUps.Add("sort by date");
        result.SuggestedFollowUps.Add("show only first 5");

        return result;
    }
}
