namespace BitbucketCodeReview.Models.Diff;

/// <summary>
/// Represents a single file that was changed in the pull request diff.
/// </summary>
public sealed class DiffFile
{
    /// <summary>File path relative to the repository root (new path after rename).</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Previous file path (only differs from FilePath when the file was renamed).</summary>
    public string OldFilePath { get; set; } = string.Empty;

    /// <summary>Whether this file was newly added (no previous version).</summary>
    public bool IsNew { get; set; }

    /// <summary>Whether this file was deleted.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Whether this file was renamed.</summary>
    public bool IsRenamed { get; set; }

    /// <summary>All changed hunks within this file.</summary>
    public List<DiffHunk> Hunks { get; set; } = [];

    /// <summary>Convenience: full unified diff text for this file only.</summary>
    public string RawDiff { get; set; } = string.Empty;
}
