using BitbucketCodeReview.Models.Diff;

namespace BitbucketCodeReview.Services.Diff;

public interface IDiffParserService
{
    /// <summary>
    /// Parses a raw unified diff string (as returned by Bitbucket's diff endpoint)
    /// into a structured list of changed files.
    /// </summary>
    IReadOnlyList<DiffFile> Parse(string rawDiff);
}
