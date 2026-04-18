using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Bitbucket;
using BitbucketCodeReview.Services.Review;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Controllers;

[ApiController]
[Route("api/webhook")]
public sealed class WebhookController : ControllerBase
{
    // Bitbucket event keys we care about
    private static readonly HashSet<string> SupportedEvents =
        ["pullrequest:created", "pullrequest:updated"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BitbucketOptions _options;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IServiceScopeFactory scopeFactory,
        IOptions<BitbucketOptions> options,
        ILogger<WebhookController> logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options.Value;
        _logger       = logger;
    }

    /// <summary>
    /// Receives Bitbucket pull request webhook events.
    /// Configure this URL in Bitbucket: Repository Settings → Webhooks → URL = /api/webhook/pullrequest
    /// Triggers: Pull Request Created, Pull Request Updated
    /// </summary>
    [HttpPost("pullrequest")]
    public async Task<IActionResult> ReceivePullRequestEvent()
    {
        // ── 1. Read raw body (needed for signature validation) ────────────────
        // CancellationToken.None: Bitbucket closes the connection immediately after
        // sending, which cancels the request token before we finish reading.
        Request.EnableBuffering();
        var rawBody = await ReadBodyAsync();

        // ── 2. Validate webhook signature if a secret is configured ───────────
        if (!string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            if (!ValidateSignature(rawBody))
            {
                _logger.LogWarning("Webhook signature validation failed — request rejected");
                return Unauthorized("Invalid webhook signature.");
            }
        }

        // ── 3. Identify the event type ────────────────────────────────────────
        var eventKey = Request.Headers["X-Event-Key"].FirstOrDefault() ?? string.Empty;

        _logger.LogInformation("Received Bitbucket event: {EventKey}", eventKey);

        if (!SupportedEvents.Contains(eventKey))
        {
            _logger.LogDebug("Ignoring unsupported event type: {EventKey}", eventKey);
            return Ok(new { message = $"Event '{eventKey}' ignored — not a PR create/update." });
        }

        // ── 4. Deserialise the payload ────────────────────────────────────────
        WebhookPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Null payload after deserialisation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialise webhook payload");
            return BadRequest("Invalid JSON payload.");
        }

        if (payload.PullRequest.Id == 0)
        {
            _logger.LogWarning("Payload contained no pull request ID — ignoring");
            return BadRequest("Missing pull request data.");
        }

        // ── 5. Kick off review asynchronously (don't block Bitbucket's timeout) ──
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reviewService = scope.ServiceProvider.GetRequiredService<ICodeReviewService>();
                await reviewService.ReviewPullRequestAsync(payload, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled exception in background review for PR #{PrId}", payload.PullRequest.Id);
            }
        }, CancellationToken.None);

        return Accepted(new
        {
            message   = "Review started.",
            prId      = payload.PullRequest.Id,
            prTitle   = payload.PullRequest.Title,
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

    /// <summary>
    /// Validates the HMAC-SHA256 signature Bitbucket attaches to webhook requests.
    /// Header: X-Hub-Signature: sha256=&lt;hex&gt;
    /// </summary>
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
        {
            _logger.LogWarning("X-Hub-Signature has unexpected format: {Header}", signatureHeader);
            return false;
        }

        var receivedHex = signatureHeader[prefix.Length..];
        var secretBytes = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var bodyBytes   = Encoding.UTF8.GetBytes(rawBody);

        var expectedHash = HMACSHA256.HashData(secretBytes, bodyBytes);
        var expectedHex  = Convert.ToHexString(expectedHash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(receivedHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHex));
    }
}
