namespace BitbucketCodeReview.Configuration;

public sealed class BitbucketOptions
{
    public const string SectionName = "Bitbucket";

    /// <summary>Bitbucket Cloud base URL. Rarely needs changing.</summary>
    public string BaseUrl { get; set; } = "https://api.bitbucket.org/2.0";

    /// <summary>Bitbucket account username (used for Basic auth).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Bitbucket App Password (Basic auth credential).</summary>
    public string AppPassword { get; set; } = string.Empty;

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
