using System.Text.Json.Serialization;

namespace BitbucketCodeReview.Models.Bitbucket;

// ─── Top-level webhook event ──────────────────────────────────────────────────

public sealed class WebhookPayload
{
    [JsonPropertyName("pullrequest")]
    public PullRequest PullRequest { get; set; } = new();

    [JsonPropertyName("repository")]
    public Repository Repository { get; set; } = new();
}

// ─── Pull Request ─────────────────────────────────────────────────────────────

public sealed class PullRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public PullRequestEndpoint Source { get; set; } = new();

    [JsonPropertyName("destination")]
    public PullRequestEndpoint Destination { get; set; } = new();

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

public sealed class PullRequestEndpoint
{
    [JsonPropertyName("branch")]
    public Branch Branch { get; set; } = new();

    [JsonPropertyName("commit")]
    public Commit Commit { get; set; } = new();

    [JsonPropertyName("repository")]
    public Repository Repository { get; set; } = new();
}

public sealed class Branch
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class Commit
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;
}

// ─── Repository ───────────────────────────────────────────────────────────────

public sealed class Repository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Derived: everything before the first slash in FullName.</summary>
    [JsonIgnore]
    public string Workspace => FullName.Contains('/')
        ? FullName.Split('/')[0]
        : string.Empty;

    /// <summary>Derived: everything after the first slash in FullName.</summary>
    [JsonIgnore]
    public string Slug => FullName.Contains('/')
        ? FullName.Split('/')[1]
        : FullName;
}
