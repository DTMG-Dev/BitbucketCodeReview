using BitbucketCodeReview.Models.Diff;
using BitbucketCodeReview.Models.Review;

namespace BitbucketCodeReview.Services.Anthropic;

public interface IAnthropicService
{
    /// <summary>
    /// Sends the diff for a single file to Claude and returns structured review comments.
    /// Returns null if the file should be skipped (empty diff, binary, etc.).
    /// <para>
    /// If <paramref name="guidelines"/> is provided (fetched from the repo's
    /// CODE_REVIEW_GUIDELINES.md), they are injected into the prompt so Claude
    /// applies project-specific rules in addition to the default criteria.
    /// </para>
    /// </summary>
    Task<FileReviewResult?> ReviewFileAsync(
        DiffFile file,
        string pullRequestTitle,
        string pullRequestDescription,
        string? guidelines = null,
        CancellationToken ct = default);
}
