using System.Text.Json.Serialization;

namespace BitbucketCodeReview.Models.Bitbucket;

/// <summary>
/// Payload sent to POST /repositories/{ws}/{repo}/pullrequests/{id}/comments
/// to create an inline PR comment on Bitbucket Cloud.
/// </summary>
public sealed class InlineCommentRequest
{
    [JsonPropertyName("content")]
    public CommentContent Content { get; set; } = new();

    [JsonPropertyName("inline")]
    public InlinePosition Inline { get; set; } = new();
}

public sealed class CommentContent
{
    [JsonPropertyName("raw")]
    public string Raw { get; set; } = string.Empty;
}

public sealed class InlinePosition
{
    /// <summary>Line number in the file to attach the comment to (1-based).</summary>
    [JsonPropertyName("to")]
    public int To { get; set; }

    /// <summary>File path relative to the repository root.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
