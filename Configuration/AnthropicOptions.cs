namespace BitbucketCodeReview.Configuration;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Claude model to use for reviews.</summary>
    public string Model { get; set; } = "claude-opus-4-6";

    /// <summary>Maximum tokens in Claude's response per file review.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Temperature (0.0–1.0). Lower = more deterministic reviews.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Path to the prompt template file (relative to content root).</summary>
    public string PromptFilePath { get; set; } = "Prompts/CodeReviewPrompt.md";
}
