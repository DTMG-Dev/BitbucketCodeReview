using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Services.Bitbucket;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.HealthChecks;

/// <summary>
/// Verifies that the Bitbucket API token is valid by calling the lightweight
/// /user endpoint. Registers as a named health check so /health can surface it.
/// </summary>
public sealed class BitbucketHealthCheck : IHealthCheck
{
    private readonly IBitbucketService _bitbucket;
    private readonly BitbucketOptions _options;

    public BitbucketHealthCheck(IBitbucketService bitbucket, IOptions<BitbucketOptions> options)
    {
        _bitbucket = bitbucket;
        _options   = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reachable = await _bitbucket.PingAsync(cancellationToken);
            return reachable
                ? HealthCheckResult.Healthy("Bitbucket API reachable")
                : HealthCheckResult.Degraded("Bitbucket API returned unexpected response");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Bitbucket API unreachable", ex);
        }
    }
}
