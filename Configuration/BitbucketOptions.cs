namespace BitbucketCodeReview.Configuration;

public sealed class BitbucketOptions
{
    public const string SectionName = "Bitbucket";

    /// <summary>Bitbucket Cloud base URL. Rarely needs changing.</summary>
    public string BaseUrl { get; set; } = "https://api.bitbucket.org/2.0";

    /// <summary>Atlassian account email address (used as username in Basic auth).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Atlassian API Token — generate at id.atlassian.com → Security → API Tokens.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret configured in the Bitbucket webhook settings.
    /// Used to validate the X-Hub-Signature header on incoming requests.
    /// Leave empty to skip signature validation (not recommended in production).
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>File extensions to include in code review (e.g. [".cs", ".ts"]).</summary>
    public List<string> ReviewableExtensions { get; set; } =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go",
        ".java", ".kt", ".rs", ".cpp", ".c", ".h"
    ];

    /// <summary>Maximum diff size in characters per file sent to Claude.</summary>
    public int MaxDiffCharsPerFile { get; set; } = 12_000;
}
