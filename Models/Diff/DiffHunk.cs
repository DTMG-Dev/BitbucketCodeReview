namespace BitbucketCodeReview.Models.Diff;

/// <summary>
/// A contiguous block of changes within a file (bounded by a @@ header).
/// </summary>
public sealed class DiffHunk
{
    /// <summary>The raw @@ header line (e.g. "@@ -10,6 +10,8 @@ context").</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Starting line number in the old (left) file.</summary>
    public int OldStart { get; set; }

    /// <summary>Starting line number in the new (right) file.</summary>
    public int NewStart { get; set; }

    /// <summary>All lines in this hunk, with their new-file line number and change type.</summary>
    public List<DiffLine> Lines { get; set; } = [];
}

public sealed class DiffLine
{
    /// <summary>Line number in the new version of the file (1-based). 0 for removed lines.</summary>
    public int NewLineNumber { get; set; }

    /// <summary>Line number in the old version of the file (1-based). 0 for added lines.</summary>
    public int OldLineNumber { get; set; }

    public DiffLineType Type { get; set; }

    /// <summary>Raw line content without the leading +/- prefix.</summary>
    public string Content { get; set; } = string.Empty;
}

public enum DiffLineType
{
    Context,  // unchanged line
    Added,    // + line (new)
    Removed   // - line (old)
}
