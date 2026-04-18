using BitbucketCodeReview.Configuration;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Infrastructure;

/// <summary>
/// Validates required configuration on startup and throws a descriptive exception
/// if any critical value is missing. Fail-fast prevents the app from running in
/// a misconfigured state and silently doing nothing.
/// </summary>
public static class StartupValidator
{
    public static void Validate(IServiceProvider services)
    {
        var errors = new List<string>();

        var bb = services.GetRequiredService<IOptions<BitbucketOptions>>().Value;
        if (string.IsNullOrWhiteSpace(bb.Email))
            errors.Add("Bitbucket:Email is required");
        if (string.IsNullOrWhiteSpace(bb.ApiToken))
            errors.Add("Bitbucket:ApiToken is required");

        var ai = services.GetRequiredService<IOptions<AnthropicOptions>>().Value;
        if (string.IsNullOrWhiteSpace(ai.ApiKey))
            errors.Add("Anthropic:ApiKey is required");

        var promptPath = Path.Combine(
            services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            ai.PromptFilePath);

        if (!File.Exists(promptPath))
            errors.Add($"Prompt file not found at '{promptPath}' (Anthropic:PromptFilePath)");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Application cannot start due to missing configuration:\n" +
                string.Join("\n", errors.Select(e => $"  • {e}")));
    }
}
