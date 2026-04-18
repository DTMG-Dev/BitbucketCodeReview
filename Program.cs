using BitbucketCodeReview.Configuration;
using BitbucketCodeReview.Services.Anthropic;
using BitbucketCodeReview.Services.Bitbucket;
using BitbucketCodeReview.Services.Diff;
using BitbucketCodeReview.Services.Review;
using Serilog;
using Serilog.Events;

// ── Serilog bootstrap logger (captures startup errors) ───────────────────────
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
        .WriteTo.File(
            "logs/review-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14));

    // ── Configuration ─────────────────────────────────────────────────────────
    builder.Services.Configure<BitbucketOptions>(
        builder.Configuration.GetSection(BitbucketOptions.SectionName));

    builder.Services.Configure<AnthropicOptions>(
        builder.Configuration.GetSection(AnthropicOptions.SectionName));

    // ── HTTP Clients ──────────────────────────────────────────────────────────
    builder.Services.AddTransient<BitbucketAuthHandler>();
    builder.Services.AddHttpClient<IBitbucketService, BitbucketService>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Disable auto-redirect so we can re-attach the auth header on each hop.
            // Bitbucket's /diff endpoint redirects to a CDN URL which needs auth too.
            AllowAutoRedirect = false
        })
        .AddHttpMessageHandler<BitbucketAuthHandler>();

    // AnthropicAuthHandler injects x-api-key + anthropic-version on every request.
    builder.Services.AddTransient<AnthropicAuthHandler>();
    builder.Services.AddHttpClient<IAnthropicService, AnthropicService>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
        .AddHttpMessageHandler<AnthropicAuthHandler>();

    // ── Application Services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<IDiffParserService, DiffParserService>();
    builder.Services.AddScoped<ICodeReviewService, CodeReviewService>();

    // ── ASP.NET Core ──────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();

    app.UseSerilogRequestLogging();
    app.MapControllers();

    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
       .WithName("HealthCheck");

    // Hot-reload the prompt file without restarting the process.
    // POST /api/admin/reload-prompt
    app.MapPost("/api/admin/reload-prompt", () =>
    {
        AnthropicService.InvalidatePromptCache();
        return Results.Ok(new { message = "Prompt cache cleared. Next review will reload from disk." });
    }).WithName("ReloadPrompt");

    Log.Information("BitbucketCodeReview starting...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
}
finally
{
    Log.CloseAndFlush();
}
