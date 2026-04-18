using System.Threading.RateLimiting;
using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.HealthChecks;
using BitbucketCodeReview.Infrastructure;
using BitbucketCodeReview.Middleware;
using BitbucketCodeReview.Services.Anthropic;
using BitbucketCodeReview.Services.Bitbucket;
using BitbucketCodeReview.Services.Diff;
using BitbucketCodeReview.Services.Review;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Serilog;

// ── Bootstrap logger (captures startup errors before full logging is ready) ───
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Logging ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/review-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14));

    // ── Configuration binding ─────────────────────────────────────────────────
    builder.Services.Configure<BitbucketOptions>(
        builder.Configuration.GetSection(BitbucketOptions.SectionName));
    builder.Services.Configure<AnthropicOptions>(
        builder.Configuration.GetSection(AnthropicOptions.SectionName));
    builder.Services.Configure<ReviewPolicyOptions>(
        builder.Configuration.GetSection(ReviewPolicyOptions.SectionName));

    // ── HTTP Clients with resilience ──────────────────────────────────────────
    builder.Services.AddTransient<BitbucketAuthHandler>();
    builder.Services.AddHttpClient<IBitbucketService, BitbucketService>()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false   // we follow redirects manually to preserve auth
        })
        .AddHttpMessageHandler<BitbucketAuthHandler>()
        .AddResilienceHandler("bitbucket", pipeline =>
        {
            // Retry up to 3 times with exponential back-off for transient errors
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay            = TimeSpan.FromSeconds(2),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true
            });
            // Circuit breaker: open after 5 failures, reset after 30 s
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio            = 0.5,
                SamplingDuration        = TimeSpan.FromSeconds(30),
                MinimumThroughput       = 5,
                BreakDuration           = TimeSpan.FromSeconds(30)
            });
            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

    builder.Services.AddTransient<AnthropicAuthHandler>();
    builder.Services.AddHttpClient<IAnthropicService, AnthropicService>()
        .AddHttpMessageHandler<AnthropicAuthHandler>()
        .AddResilienceHandler("anthropic", pipeline =>
        {
            // Fewer retries — Claude calls are expensive and slow
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay            = TimeSpan.FromSeconds(5),
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true
            });
            pipeline.AddTimeout(TimeSpan.FromSeconds(120)); // Claude can be slow
        });

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<IDiffParserService, DiffParserService>();
    builder.Services.AddSingleton<ReviewQueue>();
    builder.Services.AddSingleton<DuplicateReviewFilter>();
    builder.Services.AddSingleton<BranchFilter>();
    builder.Services.AddScoped<ICodeReviewService, CodeReviewService>();
    builder.Services.AddHostedService<ReviewWorker>();
    builder.Services.AddMemoryCache();

    // ── Rate limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        // Webhook endpoint: max 60 requests/minute from any IP
        options.AddSlidingWindowLimiter("webhook", config =>
        {
            config.PermitLimit         = 60;
            config.Window              = TimeSpan.FromMinutes(1);
            config.SegmentsPerWindow   = 6;
            config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            config.QueueLimit          = 5;
        });

        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.StatusCode = 429;
            await ctx.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Too many requests. Slow down." }, ct);
        };
    });

    // ── Health checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck<BitbucketHealthCheck>("bitbucket", tags: ["ready"])
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    // ── ASP.NET Core ──────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    // ── Build ─────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Startup validation (fail fast if misconfigured) ───────────────────────
    StartupValidator.Validate(app.Services);

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();
    else
        app.UseHttpsRedirection();

    app.UseRateLimiter();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.GetLevel = (ctx, _, _) =>
            ctx.Request.Path.StartsWithSegments("/health")
                ? Serilog.Events.LogEventLevel.Debug   // suppress noisy health-check logs
                : Serilog.Events.LogEventLevel.Information;
    });

    app.MapControllers();

    // Liveness — is the process alive?
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = h => h.Tags.Contains("live")
    });

    // Readiness — are external dependencies reachable?
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = h => h.Tags.Contains("ready")
    });

    // Combined — backward-compatible with existing /health callers
    app.MapHealthChecks("/health");

    // Admin: hot-reload the review prompt without restarting
    app.MapPost("/api/admin/reload-prompt", () =>
    {
        AnthropicService.InvalidatePromptCache();
        return Results.Ok(new { message = "Prompt cache cleared. Next review will reload from disk." });
    });

    Log.Information("BitbucketCodeReview starting on {Environment}",
        app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
