namespace BitbucketCodeReview.Configuration;

/// <summary>
/// Controls which pull requests get reviewed based on branch naming conventions.
/// All settings support wildcard patterns using * (any chars) and ? (single char).
///
/// Examples
///   SourceBranchPatterns: ["feature/*", "bugfix/*", "hotfix/*"]
///   TargetBranches:       ["main", "develop"]
///
/// Leave a list empty to allow everything for that dimension.
/// Configure via appsettings.json → ReviewPolicy section
/// or environment variables:  ReviewPolicy__TargetBranches__0=main
/// </summary>
public sealed class ReviewPolicyOptions
{
    public const string SectionName = "ReviewPolicy";

    /// <summary>
    /// Wildcard patterns for the SOURCE (feature) branch.
    /// PR is reviewed only if its source branch matches at least one pattern.
    /// Empty list = review all source branches.
    /// </summary>
    public List<string> SourceBranchPatterns { get; set; } =
    [
        "feature/*",
        "bugfix/*",
        "hotfix/*",
        "fix/*",
        "release/*"
    ];

    /// <summary>
    /// Exact names of TARGET (base) branches that trigger a review.
    /// PR is reviewed only if it targets one of these branches.
    /// Empty list = review PRs targeting any branch.
    /// </summary>
    public List<string> TargetBranches { get; set; } =
    [
        "main",
        "master",
        "develop"
    ];

    /// <summary>
    /// When true, log a detailed reason why a PR was skipped rather than
    /// just a one-liner. Useful during initial setup.
    /// </summary>
    public bool VerboseSkipLogging { get; set; } = true;
}
