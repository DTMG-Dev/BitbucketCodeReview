using System.Text;
using System.Text.RegularExpressions;
using BitbucketCodeReview.Models.Diff;

namespace BitbucketCodeReview.Services.Diff;

/// <summary>
/// Parses a unified diff string (as returned by the Bitbucket diff API) into
/// strongly-typed <see cref="DiffFile"/> objects with accurate new-file line numbers.
/// </summary>
public sealed partial class DiffParserService
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

    [GeneratedRegex(@"^diff --git a/(.+) b/(.+)$", RegexOptions.Compiled)]
    private static partial Regex DiffHeaderRegex();

    [GeneratedRegex(@"^--- (?:a/)?(.+)$", RegexOptions.Compiled)]
    private static partial Regex OldFileRegex();

    [GeneratedRegex(@"^\+\+\+ (?:b/)?(.+)$", RegexOptions.Compiled)]
    private static partial Regex NewFileRegex();

    public IReadOnlyList<DiffFile> Parse(string rawDiff)
    {
        if (string.IsNullOrWhiteSpace(rawDiff))
            return [];

        var files       = new List<DiffFile>();
        DiffFile? file  = null;
        DiffHunk? hunk  = null;
        var rawBuffer   = new StringBuilder();
        int oldLine     = 0;
        int newLine     = 0;

        foreach (var line in rawDiff.Split('\n'))
        {
            var diffMatch = DiffHeaderRegex().Match(line);
            if (diffMatch.Success)
            {
                Finalise(file, rawBuffer, files);
                file = new DiffFile
                {
                    OldFilePath = diffMatch.Groups[1].Value.Trim(),
                    FilePath    = diffMatch.Groups[2].Value.Trim()
                };
                hunk = null;
                rawBuffer.Clear();
                rawBuffer.AppendLine(line);
                continue;
            }

            if (file is null) { rawBuffer.AppendLine(line); continue; }

            rawBuffer.AppendLine(line);

            if (line.StartsWith("--- "))
            {
                var m = OldFileRegex().Match(line);
                if (m.Success) file.IsNew = m.Groups[1].Value.Trim() is "/dev/null" or "dev/null";
                continue;
            }

            if (line.StartsWith("+++ "))
            {
                var m = NewFileRegex().Match(line);
                if (m.Success)
                {
                    var path = m.Groups[1].Value.Trim();
                    file.IsDeleted = path is "/dev/null" or "dev/null";
                    if (!file.IsDeleted) file.FilePath = path;
                }
                continue;
            }

            if (line.StartsWith("rename from ") || line.StartsWith("rename to "))
            {
                file.IsRenamed = true;
                continue;
            }

            var hunkMatch = HunkHeaderRegex().Match(line);
            if (hunkMatch.Success)
            {
                hunk = new DiffHunk
                {
                    Header   = line,
                    OldStart = int.Parse(hunkMatch.Groups[1].Value),
                    NewStart = int.Parse(hunkMatch.Groups[2].Value)
                };
                file.Hunks.Add(hunk);
                oldLine = hunk.OldStart;
                newLine = hunk.NewStart;
                continue;
            }

            if (hunk is null) continue;

            if (line.StartsWith('+') && !line.StartsWith("+++"))
            {
                hunk.Lines.Add(new DiffLine { Type = DiffLineType.Added,   NewLineNumber = newLine++, Content = line[1..] });
            }
            else if (line.StartsWith('-') && !line.StartsWith("---"))
            {
                hunk.Lines.Add(new DiffLine { Type = DiffLineType.Removed, OldLineNumber = oldLine++, Content = line[1..] });
            }
            else if (line.StartsWith(' ') || line == "")
            {
                hunk.Lines.Add(new DiffLine
                {
                    Type          = DiffLineType.Context,
                    NewLineNumber = newLine++,
                    OldLineNumber = oldLine++,
                    Content       = line.Length > 0 ? line[1..] : string.Empty
                });
            }
        }

        Finalise(file, rawBuffer, files);
        return files;
    }

    private static void Finalise(DiffFile? file, StringBuilder buffer, List<DiffFile> files)
    {
        if (file is null) return;
        file.RawDiff = buffer.ToString();
        files.Add(file);
    }
}
