using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Infrastructure;
using BitbucketCodeReview.Models.Bitbucket;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Controllers;

[ApiController]
[Route("api/webhook")]
public sealed class WebhookController : ControllerBase
{
    private static readonly HashSet<string> SupportedEvents =
        ["pullrequest:created", "pullrequest:updated"];

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    private readonly ReviewQueue _queue;
    private readonly DuplicateReviewFilter _duplicateFilter;
    private readonly BitbucketOptions _options;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        ReviewQueue queue,
        DuplicateReviewFilter duplicateFilter,
        IOptions<BitbucketOptions> options,
        ILogger<WebhookController> logger)
    {
        _queue           = queue;
        _duplicateFilter = duplicateFilter;
        _options         = options.Value;
        _logger          = logger;
    }

    /// <summary>
    /// Receives Bitbucket pull request webhook events.
    /// POST /api/webhook/pullrequest
    /// </summary>
    [HttpPost("pullrequest")]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> ReceivePullRequestEvent()
    {
        // 1. Read body with CancellationToken.None — Bitbucket closes the connection
        //    almost immediately after sending, which cancels the request token.
        Request.EnableBuffering();
        var rawBody = await ReadBodyAsync();

        // 2. Validate HMAC-SHA256 signature when a secret is configured
        if (!string.IsNullOrWhiteSpace(_options.WebhookSecret) && !ValidateSignature(rawBody))
        {
            _logger.LogWarning("Webhook signature validation failed");
            return Unauthorized("Invalid webhook signature.");
        }

        // 3. Identify event type
        var eventKey = Request.Headers["X-Event-Key"].FirstOrDefault() ?? string.Empty;
        _logger.LogInformation("Received Bitbucket event: {EventKey}", eventKey);

        if (!SupportedEvents.Contains(eventKey))
            return Ok(new { message = $"Event '{eventKey}' ignored." });

        // 4. Deserialise
        WebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, JsonOpts)
                ?? throw new InvalidOperationException("Null payload.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialise webhook payload");
            return BadRequest("Invalid JSON payload.");
        }

        if (payload.PullRequest.Id == 0)
            return BadRequest("Missing pull request data.");

        // 5. Duplicate guard — same commit should not be reviewed twice
        if (!_duplicateFilter.TryMarkAsProcessing(payload))
        {
            _logger.LogInformation(
                "PR #{PrId} commit {Hash} already queued/reviewed — skipping",
                payload.PullRequest.Id,
                payload.PullRequest.Source.Commit.Hash);

            return Accepted(new { message = "Already processing.", prId = payload.PullRequest.Id });
        }

        // 6. Enqueue for the background ReviewWorker
        if (!_queue.TryEnqueue(payload))
        {
            _logger.LogWarning("Review queue is full — dropping PR #{PrId}", payload.PullRequest.Id);
            return StatusCode(503, new { message = "Review queue is full. Try again later." });
        }

        _logger.LogInformation(
            "PR #{PrId} '{Title}' queued for review (queue depth: {Depth})",
            payload.PullRequest.Id, payload.PullRequest.Title, _queue.Count);

        return Accepted(new
        {
            message  = "Review queued.",
            prId     = payload.PullRequest.Id,
            prTitle  = payload.PullRequest.Title,
            eventKey
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string> ReadBodyAsync()
    {
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(CancellationToken.None);
        Request.Body.Position = 0;
        return body;
    }

    private bool ValidateSignature(string rawBody)
    {
        var signatureHeader = Request.Headers["X-Hub-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("Missing X-Hub-Signature header");
            return false;
        }

        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var receivedHex  = signatureHeader[prefix.Length..];
        var secretBytes  = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var bodyBytes    = Encoding.UTF8.GetBytes(rawBody);
        var expectedHash = HMACSHA256.HashData(secretBytes, bodyBytes);
        var expectedHex  = Convert.ToHexString(expectedHash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(receivedHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHex));
    }
}
