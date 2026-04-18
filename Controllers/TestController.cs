using BitbucketCodeReview.Services.Bitbucket;
using Microsoft.AspNetCore.Mvc;

namespace BitbucketCodeReview.Controllers;

[ApiController]
[Route("api/test")]
public sealed class TestController : ControllerBase
{
    private readonly IBitbucketService _bitbucket;
    private readonly ILogger<TestController> _logger;

    public TestController(IBitbucketService bitbucket, ILogger<TestController> logger)
    {
        _bitbucket = bitbucket;
        _logger    = logger;
    }

    /// <summary>
    /// Posts a dummy comment to a Bitbucket PR to verify connectivity.
    /// GET /api/test/comment?workspace=myws&repo=myrepo&prId=1
    /// </summary>
    [HttpGet("comment")]
    public async Task<IActionResult> PostDummyComment(
        [FromQuery] string workspace,
        [FromQuery] string repo,
        [FromQuery] int prId)
    {
        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(repo) || prId <= 0)
            return BadRequest("Provide workspace, repo and prId query params.");

        try
        {
            await _bitbucket.PostSummaryCommentAsync(
                workspace, repo, prId,
                "🤖 **Test comment** — Bitbucket API connection is working correctly.",
                CancellationToken.None);

            _logger.LogInformation("Dummy comment posted to {Workspace}/{Repo} PR#{PrId}", workspace, repo, prId);

            return Ok(new { message = $"Comment posted to PR #{prId} in {workspace}/{repo}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post dummy comment");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
