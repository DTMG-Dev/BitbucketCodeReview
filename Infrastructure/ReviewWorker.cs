using BitbucketCodeReview.Services.Review;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Drains the <see cref="ReviewQueue"/> sequentially to avoid Anthropic rate-limit bursts.
/// Each review runs in its own DI scope so scoped services are disposed after every PR.
/// </summary>
public sealed class ReviewWorker : BackgroundService
{
    private readonly ReviewQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReviewWorker> _logger;

    public ReviewWorker(ReviewQueue queue, IServiceScopeFactory scopeFactory, ILogger<ReviewWorker> logger)
    {
        _queue        = queue;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReviewWorker started");

        await foreach (var payload in _queue.ReadAllAsync(stoppingToken))
        {
            var prId = payload.PullRequest.Id;
            try
            {
                using var scope   = _scopeFactory.CreateScope();
                var       service = scope.ServiceProvider.GetRequiredService<CodeReviewService>();
                await service.ReviewPullRequestAsync(payload, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("ReviewWorker shutting down — PR #{PrId} will not be reviewed", prId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Review failed for PR #{PrId}", prId);
            }
        }

        _logger.LogInformation("ReviewWorker stopped");
    }
}
