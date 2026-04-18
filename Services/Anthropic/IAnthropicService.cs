using BitbucketCodeReview.Models.Diff;
using BitbucketCodeReview.Models.Review;

namespace BitbucketCodeReview.Services.Anthropic;

public interface IAnthropicService
{
    /// <summary>
    /// Sends the diff for a single file to Claude and returns structured review comments.
    /// Returns null if the file should be skipped (empty diff, binary, etc.).
    /// </summary>
    Task<FileReviewResult?> ReviewFileAsync(
        DiffFile file,
        string pullRequestTitle,
        string pullRequestDescription,
        CancellationToken ct = default);
}
