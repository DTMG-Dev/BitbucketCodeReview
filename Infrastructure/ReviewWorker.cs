using BitbucketCodeReview.Services.Review;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Long-running BackgroundService that drains the <see cref="ReviewQueue"/>.
/// Reviews are processed one at a time to avoid Anthropic rate-limit bursts.
/// On graceful shutdown ASP.NET Core signals the CancellationToken which stops
/// ReadAllAsync; in-flight reviews finish naturally.
/// </summary>
public sealed class ReviewWorker : BackgroundService
{
    private readonly ReviewQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReviewWorker> _logger;

    public ReviewWorker(
        ReviewQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReviewWorker> logger)
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
                _logger.LogInformation("Dequeued PR #{PrId} for review", prId);

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICodeReviewService>();
                await service.ReviewPullRequestAsync(payload, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("ReviewWorker shutting down — PR #{PrId} will not be reviewed", prId);
                break;
            }
            catch (Exception ex)
            {
                // Log and continue — a single failure must not kill the worker
                _logger.LogError(ex, "Review failed for PR #{PrId}", prId);
            }
        }

        _logger.LogInformation("ReviewWorker stopped");
    }
}
