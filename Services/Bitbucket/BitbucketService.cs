using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Review;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Bitbucket;

public sealed class BitbucketService : IBitbucketService
{
    private readonly HttpClient _http;
    private readonly ILogger<BitbucketService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public BitbucketService(HttpClient http, IOptions<BitbucketOptions> options, ILogger<BitbucketService> logger)
    {
        _http   = http;
        _logger = logger;
        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<string> GetPullRequestDiffAsync(
        string workspace, string repoSlug, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/diff";

        // Bitbucket /diff redirects to a CDN. The DelegatingHandler won't re-run on redirect,
        // so auth is dropped. We disable AllowAutoRedirect and follow manually.
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        int hops = 0;
        while (response.StatusCode is System.Net.HttpStatusCode.Moved
                                   or System.Net.HttpStatusCode.Found
                                   or System.Net.HttpStatusCode.TemporaryRedirect
                                   or System.Net.HttpStatusCode.PermanentRedirect
               && hops++ < 5)
        {
            var location = response.Headers.Location
                ?? throw new InvalidOperationException("Redirect missing Location header.");
            _logger.LogDebug("Diff redirect → {Location}", location);
            response = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, location),
                HttpCompletionOption.ResponseHeadersRead, ct);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Diff fetch failed {Status}: {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Fetched diff for PR #{PrId} — {Length} chars", pullRequestId, body.Length);
        return body;
    }

    public async Task PostInlineCommentAsync(
        string workspace, string repoSlug, int pullRequestId, ReviewComment comment, CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/comments";

        var icon = comment.Severity switch
        {
            ReviewSeverity.Error   => "🔴",
            ReviewSeverity.Warning => "🟡",
            _                      => "🔵"
        };

        var payload = new InlineCommentRequest(
            Content: new($"{icon} **{comment.Severity}: {comment.Issue}**\n\n{comment.Suggestion}"),
            Inline:  new(To: comment.LineNumber, Path: comment.FilePath));

        await PostAsync(url, JsonSerializer.Serialize(payload, JsonOpts), ct);

        _logger.LogInformation("Inline comment → {File}:{Line} [{Severity}]",
            comment.FilePath, comment.LineNumber, comment.Severity);
    }

    public async Task PostSummaryCommentAsync(
        string workspace, string repoSlug, int pullRequestId, string markdownBody, CancellationToken ct = default)
    {
        var url  = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/comments";
        var json = JsonSerializer.Serialize(new { content = new { raw = markdownBody } }, JsonOpts);
        await PostAsync(url, json, ct);
        _logger.LogInformation("Summary comment posted to PR #{PrId}", pullRequestId);
    }

    public async Task<string?> GetRepositoryFileAsync(
        string workspace, string repoSlug, string branch, string filePath, CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/src/{Uri.EscapeDataString(branch)}/{filePath}";
        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("'{File}' not found in '{Branch}'", filePath, branch);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not fetch '{File}' from '{Branch}': {Status}", filePath, branch, response.StatusCode);
            return null;
        }

        _logger.LogInformation("Fetched '{File}' from '{Branch}' — {Length} chars", filePath, branch, body.Length);
        return body;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("user", ct);
        return response.IsSuccessStatusCode;
    }

    private async Task PostAsync(string url, string json, CancellationToken ct)
    {
        var response = await _http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("POST {Url} → {Status}\nBody: {Body}", url, response.StatusCode, body);
            throw new HttpRequestException($"Bitbucket {url} returned {(int)response.StatusCode}: {body}");
        }
    }

    // Private request/response models — only used by this service
    private record InlineCommentRequest(
        [property: JsonPropertyName("content")] CommentContent Content,
        [property: JsonPropertyName("inline")]  InlinePosition Inline);

    private record CommentContent(
        [property: JsonPropertyName("raw")] string Raw);

    private record InlinePosition(
        [property: JsonPropertyName("to")]   int    To,
        [property: JsonPropertyName("path")] string Path);
}
