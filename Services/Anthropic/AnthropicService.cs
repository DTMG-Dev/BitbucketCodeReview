using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Models.Diff;
using BitbucketCodeReview.Models.Review;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Anthropic;

/// <summary>
/// Calls the Anthropic Messages API directly via <see cref="HttpClient"/>.
/// The review prompt template is loaded from the file configured in
/// <see cref="AnthropicOptions.PromptFilePath"/> so it can be edited without recompiling.
/// </summary>
public sealed class AnthropicService : IAnthropicService
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AnthropicService> _logger;

    // Static cache — shared across all AnthropicService instances (typed HttpClient creates
    // a new instance per scope, so instance fields would be discarded after every request).
    // The file is read once per process. To hot-reload the prompt, call InvalidatePromptCache()
    // via the /api/admin/reload-prompt endpoint, or restart the process.
    private static string? _cachedPromptTemplate;
    private static readonly SemaphoreSlim _promptLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AnthropicService(
        HttpClient http,
        IOptions<AnthropicOptions> options,
        IWebHostEnvironment env,
        ILogger<AnthropicService> logger)
    {
        _http    = http;
        _options = options.Value;
        _env     = env;
        _logger  = logger;

        // Auth headers are injected by AnthropicAuthHandler (DelegatingHandler).
        // Only set the Accept header here since it is not request-specific.
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<FileReviewResult?> ReviewFileAsync(
        DiffFile file,
        string pullRequestTitle,
        string pullRequestDescription,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(file.RawDiff) || file.IsDeleted)
            return null;

        var promptTemplate = await LoadPromptTemplateAsync(ct);
        var userMessage    = BuildUserMessage(promptTemplate, file, pullRequestTitle, pullRequestDescription);

        var requestBody = new
        {
            model      = _options.Model,
            max_tokens = _options.MaxTokens,
            temperature = _options.Temperature,
            messages   = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending file {File} to Claude for review", file.FilePath);

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(MessagesEndpoint, content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP error calling Anthropic API for file {File}", file.FilePath);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Anthropic API returned {Status} for file {File}: {Body}",
                response.StatusCode, file.FilePath, responseJson);
            return null;
        }

        return ParseAnthropicResponse(responseJson, file.FilePath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildUserMessage(
        string promptTemplate,
        DiffFile file,
        string prTitle,
        string prDescription)
    {
        // Replace placeholders in the prompt template
        return promptTemplate
            .Replace("{{PR_TITLE}}", prTitle)
            .Replace("{{PR_DESCRIPTION}}", string.IsNullOrWhiteSpace(prDescription) ? "(none)" : prDescription)
            .Replace("{{FILE_PATH}}", file.FilePath)
            .Replace("{{DIFF}}", file.RawDiff);
    }

    private FileReviewResult? ParseAnthropicResponse(string responseJson, string filePath)
    {
        try
        {
            var doc  = JsonNode.Parse(responseJson);
            var text = doc?["content"]?[0]?["text"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Claude returned empty text for file {File}", filePath);
                return null;
            }

            // Extract the JSON block Claude writes between ```json ... ``` fences
            var jsonBlock = ExtractJsonBlock(text);

            if (jsonBlock is null)
            {
                _logger.LogWarning(
                    "Could not extract JSON block from Claude response for {File}. Raw: {Text}",
                    filePath, text);
                return null;
            }

            var result = JsonSerializer.Deserialize<FileReviewResult>(jsonBlock, JsonOpts);
            if (result is not null)
                result.FilePath = filePath; // ensure it matches our canonical path

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Claude response for {File}", filePath);
            return null;
        }
    }

    private static string? ExtractJsonBlock(string text)
    {
        // Claude typically wraps JSON in ```json ... ``` fences
        const string fence = "```json";
        var start = text.IndexOf(fence, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += fence.Length;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start)
                return text[start..end].Trim();
        }

        // Fallback: try to find a raw JSON object
        var braceStart = text.IndexOf('{');
        var braceEnd   = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return null;
    }

    private async Task<string> LoadPromptTemplateAsync(CancellationToken ct)
    {
        if (_cachedPromptTemplate is not null)
            return _cachedPromptTemplate;

        await _promptLock.WaitAsync(ct);
        try
        {
            if (_cachedPromptTemplate is not null)
                return _cachedPromptTemplate;

            var path = Path.Combine(_env.ContentRootPath, _options.PromptFilePath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Prompt template not found at '{path}'. " +
                    $"Check AnthropicOptions.PromptFilePath in appsettings.json.");

            _cachedPromptTemplate = await File.ReadAllTextAsync(path, ct);
            _logger.LogInformation("Loaded review prompt from {Path}", path);
            return _cachedPromptTemplate;
        }
        finally
        {
            _promptLock.Release();
        }
    }

    /// <summary>
    /// Forces a reload of the prompt template from disk on the next review request.
    /// Call via POST /api/admin/reload-prompt after editing Prompts/CodeReviewPrompt.md.
    /// </summary>
    public static void InvalidatePromptCache() => _cachedPromptTemplate = null;
}
