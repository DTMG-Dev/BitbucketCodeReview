using System.Text;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Infrastructure;
using BitbucketCodeReview.Models.Bitbucket;
using BitbucketCodeReview.Models.Review;
using BitbucketCodeReview.Services.Anthropic;
using BitbucketCodeReview.Services.Bitbucket;
using BitbucketCodeReview.Services.Diff;
using Microsoft.Extensions.Options;

namespace BitbucketCodeReview.Services.Review;

/// <summary>
/// Orchestrates the end-to-end review pipeline:
/// branch check → guidelines fetch → diff fetch → parse → AI review → post comments.
/// </summary>
public sealed class CodeReviewService
{
    private readonly IBitbucketService _bitbucket;
    private readonly IAnthropicService _anthropic;
    private readonly DiffParserService _diffParser;
    private readonly BranchFilter      _branchFilter;
    private readonly TechStackDetector _techStack;
    private readonly BitbucketOptions  _bbOptions;
    private readonly ILogger<CodeReviewService> _logger;

    public CodeReviewService(
        IBitbucketService bitbucket,
        IAnthropicService anthropic,
        DiffParserService diffParser,
        BranchFilter branchFilter,
        TechStackDetector techStack,
        IOptions<BitbucketOptions> bbOptions,
        ILogger<CodeReviewService> logger)
    {
        _bitbucket    = bitbucket;
        _anthropic    = anthropic;
        _diffParser   = diffParser;
        _branchFilter = branchFilter;
        _techStack    = techStack;
        _bbOptions    = bbOptions.Value;
        _logger       = logger;
    }

    public async Task ReviewPullRequestAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        var pr        = payload.PullRequest;
        var workspace = payload.Repository.Workspace;
        var repoSlug  = payload.Repository.Slug;
        var prId      = pr.Id;

        _logger.LogInformation("Starting review for PR #{PrId} '{Title}' in {Workspace}/{Repo}",
            prId, pr.Title, workspace, repoSlug);

        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(repoSlug))
        {
            _logger.LogError("Could not parse workspace/repo from '{FullName}'", payload.Repository.FullName);
            return;
        }

        if (!_branchFilter.ShouldReview(pr))
            return;

        await _bitbucket.PostSummaryCommentAsync(workspace, repoSlug, prId,
            "🤖 **AI Code Review started** — analysing changed files, comments will appear shortly.", ct);

        // 1. Fetch guidelines from the target branch; fall back to tech-stack detection
        var guidelines = await FetchGuidelinesAsync(workspace, repoSlug, pr.Destination.Branch.Name, ct);

        // 2. Fetch and parse the diff
        string rawDiff;
        try   { rawDiff = await _bitbucket.GetPullRequestDiffAsync(workspace, repoSlug, prId, ct); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to fetch diff for PR #{PrId}", prId); return; }

        var files = _diffParser.Parse(rawDiff);
        _logger.LogInformation("Diff has {FileCount} changed file(s)", files.Count);

        // 3. Filter to reviewable files
        var reviewable = files
            .Where(f => !f.IsDeleted && IsReviewable(f.FilePath) && f.RawDiff.Length <= _bbOptions.MaxDiffCharsPerFile)
            .ToList();

        if (reviewable.Count == 0)
        {
            _logger.LogInformation("No reviewable files in PR #{PrId}", prId);
            return;
        }

        _logger.LogInformation("{Count} file(s) will be reviewed", reviewable.Count);

        // 4. Apply tech-stack fallback if no repo guidelines were found
        guidelines ??= _techStack.BuildFallbackGuidelines(reviewable);

        // 5. Review each file, post inline comments immediately
        var allResults = new List<FileReviewResult>();

        foreach (var file in reviewable)
        {
            if (ct.IsCancellationRequested) break;

            var result = await _anthropic.ReviewFileAsync(file, pr.Title, pr.Description, guidelines, ct);
            if (result is null) continue;

            allResults.Add(result);

            foreach (var comment in result.Comments)
            {
                comment.FilePath = file.FilePath; // enforce canonical path

                if (comment.LineNumber <= 0 || !DiffHasLine(file, comment.LineNumber))
                {
                    _logger.LogDebug("Skipping comment on {File}:{Line} — line not in diff",
                        comment.FilePath, comment.LineNumber);
                    continue;
                }

                try   { await _bitbucket.PostInlineCommentAsync(workspace, repoSlug, prId, comment, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to post comment on {File}:{Line}", comment.FilePath, comment.LineNumber); }
            }
        }

        // 6. Post summary
        try   { await _bitbucket.PostSummaryCommentAsync(workspace, repoSlug, prId, BuildSummary(allResults, pr.Title), ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to post summary for PR #{PrId}", prId); }

        _logger.LogInformation("Review complete for PR #{PrId}. Files: {Files}, Comments: {Comments}",
            prId, allResults.Count, allResults.Sum(r => r.Comments.Count));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string?> FetchGuidelinesAsync(
        string workspace, string repoSlug, string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_bbOptions.GuidelinesFileName))
            return null;

        try
        {
            var content = await _bitbucket.GetRepositoryFileAsync(
                workspace, repoSlug, branch, _bbOptions.GuidelinesFileName, ct);

            if (content is not null)
                _logger.LogInformation("Loaded '{File}' from '{Branch}' — repo-specific rules applied",
                    _bbOptions.GuidelinesFileName, branch);
            else
                _logger.LogInformation("'{File}' not found in '{Branch}' — using tech-stack fallback",
                    _bbOptions.GuidelinesFileName, branch);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch '{File}' — using tech-stack fallback", _bbOptions.GuidelinesFileName);
            return null;
        }
    }

    private bool IsReviewable(string filePath)
        => _bbOptions.ReviewableExtensions.Contains(
               Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase);

    private static bool DiffHasLine(Models.Diff.DiffFile file, int lineNumber)
        => file.Hunks
               .SelectMany(h => h.Lines)
               .Any(l => l.NewLineNumber == lineNumber && l.Type != Models.Diff.DiffLineType.Removed);

    private static string BuildSummary(IReadOnlyList<FileReviewResult> results, string prTitle)
    {
        var sb    = new StringBuilder();
        var total = results.Sum(r => r.Comments.Count);

        sb.AppendLine("## 🤖 AI Code Review Complete");
        sb.AppendLine();

        if (total == 0)
        {
            sb.AppendLine("✅ **Looks good! No issues found.**");
            sb.AppendLine();
            sb.AppendLine($"Reviewed **{results.Count}** file(s) — all clear.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*Generated by BitbucketCodeReview using Claude AI.*");
            return sb.ToString();
        }

        var errors   = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Error);
        var warnings = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Warning);
        var infos    = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Info);

        sb.AppendLine(errors   > 0 ? "❌ **Changes requested** — errors need to be fixed before merging."
                    : warnings > 0 ? "⚠️ **Review with comments** — please check the warnings."
                                   : "✅ **Looks good!** — only informational notes, safe to merge.");
        sb.AppendLine();
        sb.AppendLine($"**Pull Request:** {prTitle}");
        sb.AppendLine($"**Files reviewed:** {results.Count}");
        sb.AppendLine();
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("|----------|-------|");
        sb.AppendLine($"| 🔴 Errors   | {errors}   |");
        sb.AppendLine($"| 🟡 Warnings | {warnings} |");
        sb.AppendLine($"| 🔵 Info     | {infos}    |");
        sb.AppendLine();

        foreach (var r in results.Where(r => !string.IsNullOrWhiteSpace(r.Summary)))
            sb.AppendLine($"**`{r.FilePath}`** — {r.Summary}");

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generated by BitbucketCodeReview using Claude AI.*");
        return sb.ToString();
    }
}
