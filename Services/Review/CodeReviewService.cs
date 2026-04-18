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
/// Orchestrates the end-to-end code review pipeline:
/// diff fetch → parse → AI review → post comments.
/// </summary>
public sealed class CodeReviewService : ICodeReviewService
{
    private readonly IBitbucketService _bitbucket;
    private readonly IAnthropicService _anthropic;
    private readonly IDiffParserService _diffParser;
    private readonly BranchFilter _branchFilter;
    private readonly TechStackDetector _techStackDetector;
    private readonly BitbucketOptions _bbOptions;
    private readonly ILogger<CodeReviewService> _logger;

    public CodeReviewService(
        IBitbucketService bitbucket,
        IAnthropicService anthropic,
        IDiffParserService diffParser,
        BranchFilter branchFilter,
        TechStackDetector techStackDetector,
        IOptions<BitbucketOptions> bbOptions,
        ILogger<CodeReviewService> logger)
    {
        _bitbucket          = bitbucket;
        _anthropic          = anthropic;
        _diffParser         = diffParser;
        _branchFilter       = branchFilter;
        _techStackDetector  = techStackDetector;
        _bbOptions          = bbOptions.Value;
        _logger             = logger;
    }

    public async Task ReviewPullRequestAsync(WebhookPayload payload, CancellationToken ct = default)
    {
        var pr         = payload.PullRequest;
        var repo       = payload.Repository;
        var workspace  = repo.Workspace;
        var repoSlug   = repo.Slug;
        var prId       = pr.Id;

        _logger.LogInformation(
            "Starting review for PR #{PrId} '{Title}' | workspace='{Workspace}' repo='{Repo}'",
            prId, pr.Title, workspace, repoSlug);

        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(repoSlug))
        {
            _logger.LogError(
                "Could not parse workspace/repo from payload. FullName='{FullName}'",
                payload.Repository.FullName);
            return;
        }

        // ── Branch policy check ───────────────────────────────────────────────
        // Check BEFORE posting any comment — don't spam PRs that aren't in scope.
        if (!_branchFilter.ShouldReview(pr))
            return;

        // Post immediately so we know Bitbucket connectivity works
        await _bitbucket.PostSummaryCommentAsync(workspace, repoSlug, prId,
            "🤖 **AI Code Review started** — analysing changed files, comments will appear shortly.",
            ct);

        // 1. Fetch per-repo review guidelines from the target branch (best-effort)
        //    Fallback: auto-detect tech stack from the diff and inject stack-specific rules.
        var targetBranch = pr.Destination.Branch.Name;
        string? guidelines = null;
        bool usingRepoGuidelines = false;

        if (!string.IsNullOrWhiteSpace(_bbOptions.GuidelinesFileName))
        {
            try
            {
                guidelines = await _bitbucket.GetRepositoryFileAsync(
                    workspace, repoSlug, targetBranch, _bbOptions.GuidelinesFileName, ct);

                if (guidelines is not null)
                {
                    usingRepoGuidelines = true;
                    _logger.LogInformation(
                        "Loaded '{GuidelinesFile}' from '{Branch}' — repo-specific rules will be applied.",
                        _bbOptions.GuidelinesFileName, targetBranch);
                }
                else
                {
                    _logger.LogInformation(
                        "No '{GuidelinesFile}' in '{Branch}' — will fall back to tech-stack detection.",
                        _bbOptions.GuidelinesFileName, targetBranch);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch '{GuidelinesFile}' — will fall back to tech-stack detection.",
                    _bbOptions.GuidelinesFileName);
            }
        }

        // 2. Fetch the raw diff
        string rawDiff;
        try
        {
            rawDiff = await _bitbucket.GetPullRequestDiffAsync(workspace, repoSlug, prId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch diff for PR #{PrId}", prId);
            return;
        }

        // 3. Parse the diff into files
        var files = _diffParser.Parse(rawDiff);

        _logger.LogInformation("Diff contains {FileCount} changed files", files.Count);

        // 4. Filter to reviewable file types and size limits
        var reviewable = files
            .Where(f => !f.IsDeleted)
            .Where(f => IsReviewableExtension(f.FilePath))
            .Where(f => f.RawDiff.Length <= _bbOptions.MaxDiffCharsPerFile)
            .ToList();

        if (reviewable.Count == 0)
        {
            _logger.LogInformation("No reviewable files in PR #{PrId}", prId);
            return;
        }

        _logger.LogInformation("{Count} files will be sent for review", reviewable.Count);

        // 4b. Apply tech-stack fallback if no repo guidelines were found.
        //     We do this after filtering so we only look at reviewable files.
        if (!usingRepoGuidelines)
            guidelines = _techStackDetector.BuildFallbackGuidelines(reviewable);

        // 5. Review each file in sequence (avoids rate-limit bursts)
        var allResults = new List<FileReviewResult>();

        foreach (var file in reviewable)
        {
            if (ct.IsCancellationRequested) break;

            var result = await _anthropic.ReviewFileAsync(
                file, pr.Title, pr.Description, guidelines, ct);

            if (result is null) continue;

            allResults.Add(result);

            // 6. Post inline comments for this file immediately
            foreach (var comment in result.Comments)
            {
                // Always override the file path with the canonical path from the diff.
                // Claude may return a slightly different casing or prefix; Bitbucket's API
                // requires the exact path as it appears in the diff header.
                comment.FilePath = file.FilePath;

                // Only post comments on lines that were actually added/changed in this diff.
                if (comment.LineNumber <= 0) continue;

                // Verify the line number exists in the diff so we don't send a stale reference.
                if (!DiffContainsNewLine(file, comment.LineNumber))
                {
                    _logger.LogWarning(
                        "Skipping comment on {File}:{Line} — line not found in diff",
                        comment.FilePath, comment.LineNumber);
                    continue;
                }

                try
                {
                    await _bitbucket.PostInlineCommentAsync(
                        workspace, repoSlug, prId, comment, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to post comment on {File}:{Line}",
                        comment.FilePath, comment.LineNumber);
                }
            }
        }

        // 7. Post overall summary comment
        var totalComments = allResults.Sum(r => r.Comments.Count);
        var summaryText   = BuildSummaryComment(allResults, pr.Title, totalComments);

        try
        {
            await _bitbucket.PostSummaryCommentAsync(workspace, repoSlug, prId, summaryText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to post summary comment on PR #{PrId}", prId);
        }

        _logger.LogInformation(
            "Review complete for PR #{PrId}. Files reviewed: {Count}. Total comments: {Comments}",
            prId, allResults.Count,
            allResults.Sum(r => r.Comments.Count));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsReviewableExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return _bbOptions.ReviewableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the given new-file line number appears as an added or context
    /// line in any of the file's hunks. This prevents Claude's hallucinated line
    /// numbers from reaching the Bitbucket API.
    /// </summary>
    private static bool DiffContainsNewLine(Models.Diff.DiffFile file, int lineNumber)
    {
        return file.Hunks
            .SelectMany(h => h.Lines)
            .Any(l => l.NewLineNumber == lineNumber &&
                      l.Type != Models.Diff.DiffLineType.Removed);
    }

    private static string BuildSummaryComment(
        IReadOnlyList<FileReviewResult> results,
        string prTitle,
        int totalComments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 🤖 AI Code Review Complete");
        sb.AppendLine();

        // ── No reviewable files or Claude found nothing ───────────────────────
        if (totalComments == 0)
        {
            sb.AppendLine("✅ **Looks good! No issues found.**");
            sb.AppendLine();
            sb.AppendLine($"Reviewed **{results.Count}** file(s) — all clear.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("*Generated by BitbucketCodeReview using Claude AI.*");
            return sb.ToString();
        }

        // ── Issues found ─────────────────────────────────────────────────────
        var errors   = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Error);
        var warnings = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Warning);
        var infos    = results.SelectMany(r => r.Comments).Count(c => c.Severity == ReviewSeverity.Info);

        var verdict = errors > 0
            ? "❌ **Changes requested** — errors need to be fixed before merging."
            : warnings > 0
                ? "⚠️ **Review with comments** — please check the warnings."
                : "✅ **Looks good!** — only informational notes, safe to merge.";

        sb.AppendLine(verdict);
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

        foreach (var file in results.Where(r => !string.IsNullOrWhiteSpace(r.Summary)))
        {
            sb.AppendLine($"**`{file.FilePath}`** — {file.Summary}");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*Generated by BitbucketCodeReview using Claude AI.*");

        return sb.ToString();
    }
}
