using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Bitbucket;
using BitbucketCodeReview.Models.Review;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Bitbucket;

/// <summary>
/// Wraps the Bitbucket Cloud REST API v2.0 for diff retrieval and comment posting.
/// Authentication uses HTTP Basic Auth (username + App Password).
/// </summary>
public sealed class BitbucketService : IBitbucketService
{
    private readonly HttpClient _http;
    private readonly BitbucketOptions _options;
    private readonly ILogger<BitbucketService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BitbucketService(
        HttpClient http,
        IOptions<BitbucketOptions> options,
        ILogger<BitbucketService> logger)
    {
        _http    = http;
        _options = options.Value;
        _logger  = logger;

        // Basic auth header
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.Username}:{_options.AppPassword}"));

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    // ── Diff ──────────────────────────────────────────────────────────────────

    public async Task<string> GetPullRequestDiffAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/diff";

        _logger.LogInformation(
            "Fetching diff for PR #{PrId} in {Workspace}/{Repo}",
            pullRequestId, workspace, repoSlug);

        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Bitbucket diff API returned {Status}: {Body}",
                response.StatusCode, body);
            response.EnsureSuccessStatusCode(); // throw
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    public async Task PostInlineCommentAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        ReviewComment comment,
        CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/comments";

        var severityIcon = comment.Severity switch
        {
            ReviewSeverity.Error   => "🔴",
            ReviewSeverity.Warning => "🟡",
            _                      => "🔵"
        };

        var body = $"{severityIcon} **{comment.Severity}: {comment.Issue}**\n\n{comment.Suggestion}";

        var payload = new InlineCommentRequest
        {
            Content = new CommentContent { Raw = body },
            Inline  = new InlinePosition
            {
                To   = comment.LineNumber,
                Path = comment.FilePath
            }
        };

        await PostCommentAsync(url, payload, ct);

        _logger.LogInformation(
            "Posted {Severity} comment on {File}:{Line}",
            comment.Severity, comment.FilePath, comment.LineNumber);
    }

    public async Task PostSummaryCommentAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        string markdownBody,
        CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/comments";

        var payload = new { content = new { raw = markdownBody } };
        var json    = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to post summary comment: {Status} {Body}",
                response.StatusCode, responseBody);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task PostCommentAsync(string url, InlineCommentRequest payload, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(payload, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Bitbucket comment API returned {Status}: {Body}",
                response.StatusCode, body);
        }
    }
}
