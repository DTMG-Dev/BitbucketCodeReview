namespace BitbucketCodeReview.Models.Diff;

public sealed class DiffFile
{
    public string FilePath    { get; set; } = string.Empty;
    public string OldFilePath { get; set; } = string.Empty;
    public bool   IsNew       { get; set; }
    public bool   IsDeleted   { get; set; }
    public bool   IsRenamed   { get; set; }
    public List<DiffHunk> Hunks { get; set; } = [];
    public string RawDiff       { get; set; } = string.Empty;
}

public sealed class DiffHunk
{
    public string Header   { get; set; } = string.Empty;
    public int    OldStart { get; set; }
    public int    NewStart { get; set; }
    public List<DiffLine> Lines { get; set; } = [];
}

public sealed class DiffLine
{
    public int          NewLineNumber { get; set; }
    public int          OldLineNumber { get; set; }
    public DiffLineType Type          { get; set; }
    public string       Content       { get; set; } = string.Empty;
}

public enum DiffLineType { Context, Added, Removed }
