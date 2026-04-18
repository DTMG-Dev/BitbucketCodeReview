using System.Net.Http.Headers;
using System.Text;
using BitbucketCodeReview.Configuration;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Bitbucket;

/// <summary>
/// Injects Basic auth into every Bitbucket API request.
/// Atlassian API Tokens use Basic auth: email address as username, token as password.
/// </summary>
public sealed class BitbucketAuthHandler : DelegatingHandler
{
    private readonly string _encodedCredentials;

    public BitbucketAuthHandler(IOptions<BitbucketOptions> options)
    {
        var opts = options.Value;
        _encodedCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{opts.Email}:{opts.ApiToken}"));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", _encodedCredentials);

        return base.SendAsync(request, cancellationToken);
    }
}
