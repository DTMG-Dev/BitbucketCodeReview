using System.Text.Json.Serialization;

namespace BitbucketCodeReview.Models.Bitbucket;

public sealed class WebhookPayload
{
    [JsonPropertyName("pullrequest")]
    public PullRequest PullRequest { get; set; } = new();

    [JsonPropertyName("repository")]
    public Repository Repository { get; set; } = new();
}

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

public sealed class Repository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Workspace
    {
        get
        {
            var slash = FullName.IndexOf('/');
            return slash >= 0 ? FullName[..slash] : string.Empty;
        }
    }

    [JsonIgnore]
    public string Slug
    {
        get
        {
            var slash = FullName.IndexOf('/');
            return slash >= 0 ? FullName[(slash + 1)..] : FullName;
        }
    }
}
