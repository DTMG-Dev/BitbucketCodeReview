using BitbucketCodeReview.Models.Review;

namespace BitbucketCodeReview.Services.Bitbucket;

public interface IBitbucketService
{
    /// <summary>
    /// Fetches the raw unified diff for a pull request from the Bitbucket Cloud API.
    /// </summary>
    Task<string> GetPullRequestDiffAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        CancellationToken ct = default);

    /// <summary>
    /// Posts an inline comment on a specific line of a file in the pull request.
    /// </summary>
    Task PostInlineCommentAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        ReviewComment comment,
        CancellationToken ct = default);

    /// <summary>
    /// Posts a general (non-inline) comment summarising the overall review on the PR.
    /// </summary>
    Task PostSummaryCommentAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        string markdownBody,
        CancellationToken ct = default);
}
