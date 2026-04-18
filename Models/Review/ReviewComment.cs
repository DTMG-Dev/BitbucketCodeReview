using System.Text.Json.Serialization;

namespace BitbucketCodeReview.Models.Review;

/// <summary>
/// A single inline comment returned by Claude after reviewing a diff.
/// </summary>
public sealed class ReviewComment
{
    /// <summary>File path relative to the repository root.</summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number in the new version of the file (1-based).
    /// Should reference a line that exists in the diff.
    /// </summary>
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    /// <summary>Severity level of the finding.</summary>
    [JsonPropertyName("severity")]
    public ReviewSeverity Severity { get; set; }

    /// <summary>Short title / category of the issue (e.g. "Null Reference Risk").</summary>
    [JsonPropertyName("issue")]
    public string Issue { get; set; } = string.Empty;

    /// <summary>Detailed explanation and suggestion for improvement.</summary>
    [JsonPropertyName("suggestion")]
    public string Suggestion { get; set; } = string.Empty;
}

/// <summary>
/// The structured envelope that Claude returns for each file it reviews.
/// </summary>
public sealed class FileReviewResult
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("comments")]
    public List<ReviewComment> Comments { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReviewSeverity
{
    Info,
    Warning,
    Error
}
