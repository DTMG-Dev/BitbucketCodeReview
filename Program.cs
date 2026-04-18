using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/review-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

    builder.Services.Configure<BitbucketOptions>(builder.Configuration.GetSection(BitbucketOptions.SectionName));
    builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
    builder.Services.Configure<ReviewPolicyOptions>(builder.Configuration.GetSection(ReviewPolicyOptions.SectionName));

    // HTTP clients
    builder.Services.AddTransient<BitbucketAuthHandler>();
    builder.Services.AddHttpClient<IBitbucketService, BitbucketService>()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
        .AddHttpMessageHandler<BitbucketAuthHandler>()
        .AddStandardResilienceHandler()
        .Configure(o =>
        {
            o.Retry.MaxRetryAttempts          = 3;
            o.Retry.Delay                     = TimeSpan.FromSeconds(2);
            o.Retry.UseJitter                 = true;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            o.CircuitBreaker.MinimumThroughput = 5;
            o.CircuitBreaker.BreakDuration    = TimeSpan.FromSeconds(30);
            o.TotalRequestTimeout.Timeout     = TimeSpan.FromSeconds(30);
        });

    builder.Services.AddTransient<AnthropicAuthHandler>();
    builder.Services.AddHttpClient<IAnthropicService, AnthropicService>()
        .AddHttpMessageHandler<AnthropicAuthHandler>()
        .AddStandardResilienceHandler()
        .Configure(o =>
        {
            o.Retry.MaxRetryAttempts          = 2;
            o.Retry.Delay                     = TimeSpan.FromSeconds(5);
            o.Retry.UseJitter                 = true;
            o.TotalRequestTimeout.Timeout     = TimeSpan.FromSeconds(120);
            o.AttemptTimeout.Timeout          = TimeSpan.FromSeconds(110);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(240); // must be >= 2× AttemptTimeout
        });

    // Application services
    builder.Services.AddSingleton<DiffParserService>();
    builder.Services.AddSingleton<ReviewQueue>();
    builder.Services.AddSingleton<DuplicateReviewFilter>();
    builder.Services.AddSingleton<BranchFilter>();
    builder.Services.AddSingleton<TechStackDetector>();
    builder.Services.AddScoped<CodeReviewService>();
    builder.Services.AddHostedService<ReviewWorker>();
    builder.Services.AddMemoryCache();

    // Rate limiting — max 60 webhook requests/minute per IP
    builder.Services.AddRateLimiter(o =>
    {
        o.AddSlidingWindowLimiter("webhook", c =>
        {
            c.PermitLimit          = 60;
            c.Window               = TimeSpan.FromMinutes(1);
            c.SegmentsPerWindow    = 6;
            c.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            c.QueueLimit           = 5;
        });
        o.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.StatusCode = 429;
            await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests." }, ct);
        };
    });

    builder.Services.AddHealthChecks()
        .AddCheck<BitbucketHealthCheck>("bitbucket", tags: ["ready"])
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    StartupValidator.Validate(app.Services);

    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();
    else
        app.UseHttpsRedirection();

    app.UseRateLimiter();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.GetLevel = (ctx, _, _) => ctx.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
    });

    app.MapControllers();
    app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = h => h.Tags.Contains("live")  });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = h => h.Tags.Contains("ready") });
    app.MapHealthChecks("/health");

    app.MapPost("/api/admin/reload-prompt", () =>
    {
        AnthropicService.InvalidatePromptCache();
        return Results.Ok(new { message = "Prompt cache cleared. Next review will reload from disk." });
    });

    Log.Information("BitbucketCodeReview starting on {Environment}", app.Environment.EnvironmentName);
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
