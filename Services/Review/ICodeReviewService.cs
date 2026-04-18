using BitbucketCodeReview.Models.Bitbucket;

namespace BitbucketCodeReview.Services.Review;

public interface ICodeReviewService
{
    /// <summary>
    /// Orchestrates the full review pipeline for a pull request:
    /// 1. Fetch the PR diff from Bitbucket.
    /// 2. Parse the diff into individual files.
    /// 3. Send each eligible file to Claude for review.
    /// 4. Post inline comments and a summary back to Bitbucket.
    /// </summary>
    Task ReviewPullRequestAsync(WebhookPayload payload, CancellationToken ct = default);
}
