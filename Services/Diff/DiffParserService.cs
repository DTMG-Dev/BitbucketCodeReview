using System.Text;
using System.Text.RegularExpressions;
using BitbucketCodeReview.Models.Diff;

namespace BitbucketCodeReview.Services.Diff;

/// <summary>
/// Parses a unified diff string into strongly-typed <see cref="DiffFile"/> objects.
/// Handles standard git diff output as produced by the Bitbucket diff API.
/// </summary>
public sealed partial class DiffParserService : IDiffParserService
{
    // Matches:  @@ -10,6 +10,8 @@ optional context text
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

    // Matches:  diff --git a/path b/path
    [GeneratedRegex(@"^diff --git a/(.+) b/(.+)$", RegexOptions.Compiled)]
    private static partial Regex DiffHeaderRegex();

    // Matches:  --- a/path  or  --- /dev/null
    [GeneratedRegex(@"^--- (?:a/)?(.+)$", RegexOptions.Compiled)]
    private static partial Regex OldFileRegex();

    // Matches:  +++ b/path  or  +++ /dev/null
    [GeneratedRegex(@"^\+\+\+ (?:b/)?(.+)$", RegexOptions.Compiled)]
    private static partial Regex NewFileRegex();

    public IReadOnlyList<DiffFile> Parse(string rawDiff)
    {
        if (string.IsNullOrWhiteSpace(rawDiff))
            return [];

        var files = new List<DiffFile>();
        DiffFile? currentFile = null;
        DiffHunk? currentHunk = null;
        var rawFileBuffer = new StringBuilder();

        int oldLine = 0;
        int newLine = 0;

        foreach (var line in rawDiff.Split('\n'))
        {
            // ── New file boundary ─────────────────────────────────────────────
            var diffMatch = DiffHeaderRegex().Match(line);
            if (diffMatch.Success)
            {
                FinaliseFile(currentFile, rawFileBuffer, files);

                currentFile = new DiffFile
                {
                    OldFilePath = diffMatch.Groups[1].Value.Trim(),
                    FilePath    = diffMatch.Groups[2].Value.Trim()
                };
                currentHunk = null;
                rawFileBuffer.Clear();
                rawFileBuffer.AppendLine(line);
                continue;
            }

            if (currentFile is null)
            {
                rawFileBuffer.AppendLine(line);
                continue;
            }

            rawFileBuffer.AppendLine(line);

            // ── Old/new file headers ──────────────────────────────────────────
            if (line.StartsWith("--- "))
            {
                var m = OldFileRegex().Match(line);
                if (m.Success)
                {
                    var path = m.Groups[1].Value.Trim();
                    currentFile.IsNew = path is "/dev/null" or "dev/null";
                }
                continue;
            }

            if (line.StartsWith("+++ "))
            {
                var m = NewFileRegex().Match(line);
                if (m.Success)
                {
                    var path = m.Groups[1].Value.Trim();
                    currentFile.IsDeleted = path is "/dev/null" or "dev/null";
                    if (!currentFile.IsDeleted)
                        currentFile.FilePath = path; // use the +++ path as canonical
                }
                continue;
            }

            // ── Rename detection ──────────────────────────────────────────────
            if (line.StartsWith("rename from ") || line.StartsWith("rename to "))
            {
                currentFile.IsRenamed = true;
                continue;
            }

            // ── Hunk header ───────────────────────────────────────────────────
            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                currentHunk = new DiffHunk
                {
                    Header   = line,
                    OldStart = int.Parse(hunkMatch.Groups[1].Value),
                    NewStart = int.Parse(hunkMatch.Groups[2].Value)
                };
                currentFile.Hunks.Add(currentHunk);

                oldLine = currentHunk.OldStart;
                newLine = currentHunk.NewStart;
                continue;
            }

            // ── Diff content lines ────────────────────────────────────────────
            if (currentHunk is null) continue;

            if (line.StartsWith('+') && !line.StartsWith("+++"))
            {
                currentHunk.Lines.Add(new DiffLine
                {
                    Type          = DiffLineType.Added,
                    NewLineNumber = newLine,
                    OldLineNumber = 0,
                    Content       = line[1..]
                });
                newLine++;
            }
            else if (line.StartsWith('-') && !line.StartsWith("---"))
            {
                currentHunk.Lines.Add(new DiffLine
                {
                    Type          = DiffLineType.Removed,
                    NewLineNumber = 0,
                    OldLineNumber = oldLine,
                    Content       = line[1..]
                });
                oldLine++;
            }
            else if (line.StartsWith(' ') || line == "")
            {
                // Context line
                currentHunk.Lines.Add(new DiffLine
                {
                    Type          = DiffLineType.Context,
                    NewLineNumber = newLine,
                    OldLineNumber = oldLine,
                    Content       = line.Length > 0 ? line[1..] : string.Empty
                });
                oldLine++;
                newLine++;
            }
        }

        FinaliseFile(currentFile, rawFileBuffer, files);
        return files;
    }

    private static void FinaliseFile(
        DiffFile? file,
        StringBuilder rawBuffer,
        List<DiffFile> files)
    {
        if (file is null) return;
        file.RawDiff = rawBuffer.ToString();
        files.Add(file);
    }
}
