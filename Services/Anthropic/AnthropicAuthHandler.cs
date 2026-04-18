using BitbucketCodeReview.Configuration;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Anthropic;

/// <summary>
/// DelegatingHandler that injects Anthropic authentication headers into every outbound request
/// made by <see cref="AnthropicService"/>. Registered as a transient typed handler so
/// headers are not duplicated when <see cref="HttpClient"/> instances are reused from the pool.
/// </summary>
public sealed class AnthropicAuthHandler : DelegatingHandler
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly string _apiKey;

    public AnthropicAuthHandler(IOptions<AnthropicOptions> options)
    {
        _apiKey = options.Value.ApiKey;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        return base.SendAsync(request, cancellationToken);
    }
}
