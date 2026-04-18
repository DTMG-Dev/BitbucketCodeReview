using System.Text.RegularExpressions;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Bitbucket;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Decides whether a pull request should be reviewed based on the branch
/// naming conventions configured in <see cref="ReviewPolicyOptions"/>.
/// </summary>
public sealed class BranchFilter
{
    private readonly ReviewPolicyOptions _policy;
    private readonly ILogger<BranchFilter> _logger;

    public BranchFilter(IOptions<ReviewPolicyOptions> policy, ILogger<BranchFilter> logger)
    {
        _policy = policy.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if the PR passes all configured branch rules.
    /// Returns false (with a log message) if it should be skipped.
    /// </summary>
    public bool ShouldReview(PullRequest pr)
    {
        var sourceBranch = pr.Source.Branch.Name;
        var targetBranch = pr.Destination.Branch.Name;

        // ── Source branch check ───────────────────────────────────────────────
        if (_policy.SourceBranchPatterns.Count > 0)
        {
            var matched = _policy.SourceBranchPatterns
                .Any(p => MatchesWildcard(sourceBranch, p));

            if (!matched)
            {
                if (_policy.VerboseSkipLogging)
                    _logger.LogInformation(
                        "PR #{PrId} skipped — source branch '{Source}' does not match any pattern in ReviewPolicy:SourceBranchPatterns [{Patterns}]",
                        pr.Id, sourceBranch,
                        string.Join(", ", _policy.SourceBranchPatterns));
                else
                    _logger.LogInformation(
                        "PR #{PrId} skipped — source branch '{Source}' not in allowed patterns",
                        pr.Id, sourceBranch);

                return false;
            }
        }

        // ── Target branch check ───────────────────────────────────────────────
        if (_policy.TargetBranches.Count > 0)
        {
            var allowed = _policy.TargetBranches
                .Any(t => string.Equals(t, targetBranch, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                if (_policy.VerboseSkipLogging)
                    _logger.LogInformation(
                        "PR #{PrId} skipped — target branch '{Target}' is not in ReviewPolicy:TargetBranches [{Allowed}]",
                        pr.Id, targetBranch,
                        string.Join(", ", _policy.TargetBranches));
                else
                    _logger.LogInformation(
                        "PR #{PrId} skipped — target branch '{Target}' not in allowed targets",
                        pr.Id, targetBranch);

                return false;
            }
        }

        _logger.LogInformation(
            "PR #{PrId} '{Source}' → '{Target}' passed branch policy check",
            pr.Id, sourceBranch, targetBranch);

        return true;
    }

    // ── Wildcard matching ─────────────────────────────────────────────────────

    /// <summary>
    /// Matches a branch name against a wildcard pattern.
    ///   *  matches any sequence of characters (including slashes)
    ///   ?  matches exactly one character
    /// Examples:
    ///   "feature/*"  matches "feature/TICKET-123"
    ///   "release/v?" matches "release/v1"
    ///   "hotfix"     matches only "hotfix" (exact)
    /// </summary>
    private static bool MatchesWildcard(string input, string pattern)
    {
        // Convert wildcard to regex
        var regexPattern = "^" +
            Regex.Escape(pattern)
                 .Replace(@"\*", ".*")
                 .Replace(@"\?", ".") +
            "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
