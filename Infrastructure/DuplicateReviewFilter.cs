using BitbucketCodeReview.Models.Bitbucket;
using Microsoft.Extensions.Caching.Memory;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Prevents the same PR commit from being reviewed twice.
/// Bitbucket fires pullrequest:updated on every push — without this filter
/// each push triggers a full re-review even if nothing changed.
/// </summary>
public sealed class DuplicateReviewFilter
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public DuplicateReviewFilter(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Returns true (and marks as seen) if this payload has not been processed recently.
    /// Returns false if it was already processed — caller should skip the review.
    /// </summary>
    public bool TryMarkAsProcessing(WebhookPayload payload)
    {
        var key = CacheKey(payload);

        if (_cache.TryGetValue(key, out _))
            return false; // already seen

        _cache.Set(key, true, Ttl);
        return true;
    }

    private static string CacheKey(WebhookPayload p)
    {
        var commitHash = p.PullRequest.Source.Commit.Hash;

        // Fall back to PR ID only if commit hash is missing
        return string.IsNullOrWhiteSpace(commitHash)
            ? $"pr:{p.PullRequest.Id}"
            : $"pr:{p.PullRequest.Id}:commit:{commitHash}";
    }
}
