using System.Text;
using System.Text.Json;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Bitbucket;
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
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BitbucketService(
        HttpClient http,
        IOptions<BitbucketOptions> options,
        ILogger<BitbucketService> logger)
    {
        _http   = http;
        _logger = logger;

        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");

        _logger.LogInformation("BitbucketService base URL: {BaseUrl}", _http.BaseAddress);
    }

    // ── Diff ──────────────────────────────────────────────────────────────────

    public async Task<string> GetPullRequestDiffAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        CancellationToken ct = default)
    {
        var url = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/diff";

        _logger.LogInformation("GET {Url}", url);

        // Bitbucket's /diff endpoint redirects to a CDN URL.
        // HttpClient follows the redirect but the DelegatingHandler does NOT run again,
        // so the auth header is lost. We follow redirects manually to preserve auth.
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        _logger.LogInformation("GET {Url} → {Status}", url, response.StatusCode);

        // Follow up to 5 redirects manually, re-attaching auth each time
        int redirects = 0;
        while (response.StatusCode is System.Net.HttpStatusCode.Moved
                                   or System.Net.HttpStatusCode.Found
                                   or System.Net.HttpStatusCode.TemporaryRedirect
                                   or System.Net.HttpStatusCode.PermanentRedirect
               && redirects++ < 5)
        {
            var location = response.Headers.Location
                ?? throw new InvalidOperationException("Redirect with no Location header.");

            _logger.LogInformation("Following redirect → {Location}", location);

            var redirectRequest = new HttpRequestMessage(HttpMethod.Get, location);
            response = await _http.SendAsync(redirectRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            _logger.LogInformation("Redirect {Location} → {Status}", location, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Diff fetch failed ({Status}). Body: {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Diff fetched — {Length} chars", body.Length);
        return body;
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

        var commentBody = $"{severityIcon} **{comment.Severity}: {comment.Issue}**\n\n{comment.Suggestion}";

        var payload = new InlineCommentRequest
        {
            Content = new CommentContent { Raw = commentBody },
            Inline  = new InlinePosition { To = comment.LineNumber, Path = comment.FilePath }
        };

        await PostAsync(url, JsonSerializer.Serialize(payload, JsonOpts), ct);

        _logger.LogInformation(
            "Inline comment posted → {File}:{Line} [{Severity}]",
            comment.FilePath, comment.LineNumber, comment.Severity);
    }

    public async Task PostSummaryCommentAsync(
        string workspace,
        string repoSlug,
        int pullRequestId,
        string markdownBody,
        CancellationToken ct = default)
    {
        var url     = $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/comments";
        var payload = new { content = new { raw = markdownBody } };
        var json    = JsonSerializer.Serialize(payload, JsonOpts);

        await PostAsync(url, json, ct);

        _logger.LogInformation("Summary comment posted to PR #{PrId}", pullRequestId);
    }

    // ── Repository file fetch ─────────────────────────────────────────────────

    public async Task<string?> GetRepositoryFileAsync(
        string workspace,
        string repoSlug,
        string branch,
        string filePath,
        CancellationToken ct = default)
    {
        // Bitbucket src API: GET /2.0/repositories/{workspace}/{slug}/src/{node}/{path}
        // {node} can be a branch name; returns the raw file content directly.
        var url = $"repositories/{workspace}/{repoSlug}/src/{Uri.EscapeDataString(branch)}/{filePath}";

        _logger.LogInformation("Fetching repo file: GET {Url}", url);

        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "'{FilePath}' not found in '{Branch}' — no repo-specific guidelines will be used.",
                filePath, branch);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Could not fetch '{FilePath}' from '{Branch}': {Status} — skipping guidelines.",
                filePath, branch, response.StatusCode);
            return null;
        }

        _logger.LogInformation(
            "Fetched '{FilePath}' from '{Branch}' — {Length} chars",
            filePath, branch, body.Length);

        return body;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("user", ct);
        return response.IsSuccessStatusCode;
    }

    // ── Core POST helper with full logging ────────────────────────────────────

    private async Task PostAsync(string url, string json, CancellationToken ct)
    {
        _logger.LogDebug("POST {Url} | Body: {Json}", url, json);

        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("POST {Url} → {Status} ✓", url, response.StatusCode);
        }
        else
        {
            _logger.LogError(
                "POST {Url} → {Status} ✗\nRequest JSON: {Json}\nResponse: {Body}",
                url, response.StatusCode, json, body);

            throw new HttpRequestException(
                $"Bitbucket API {url} returned {(int)response.StatusCode}: {body}");
        }
    }
}
