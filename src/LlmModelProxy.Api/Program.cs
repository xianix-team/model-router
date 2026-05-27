using LlmModelProxy.Api.Middleware;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Pipeline;
using LlmModelProxy.Core.Pipeline.Behaviors;
using LlmModelProxy.Infrastructure;
using LlmModelProxy.Providers;
using Serilog;

// ── Bootstrap Serilog early so startup errors are captured ───────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console());

    // ── Configuration ─────────────────────────────────────────────────────────
    builder.Services.Configure<ProxyOptions>(
        builder.Configuration.GetSection(ProxyOptions.Section));
    // Bind the nested Pipeline section separately so IOptions<PipelineOptions>
    // is resolvable by the behaviour classes and translators.
    builder.Services.Configure<PipelineOptions>(
        builder.Configuration.GetSection("Proxy:Pipeline"));

    // ── Pipeline behaviours ───────────────────────────────────────────────────
    builder.Services.AddScoped<SystemPromptInjectionBehavior>();
    builder.Services.AddScoped<SyntheticToolInjectionBehavior>();
    builder.Services.AddScoped<ProxyPipeline>(sp =>
        new ProxyPipeline(
        [
            sp.GetRequiredService<SystemPromptInjectionBehavior>(),
            sp.GetRequiredService<SyntheticToolInjectionBehavior>()
        ]));

    // ── Infrastructure ────────────────────────────────────────────────────────
    builder.Services.AddProxyInfrastructure();

    // ── Providers & router ────────────────────────────────────────────────────
    builder.Services.AddLlmProviders(builder.Configuration);

    // ── ASP.NET Core ──────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            o.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // ── Middleware pipeline ────────────────────────────────────────────────────
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.MapControllers();
    app.MapHealthChecks("/health");

    // ── Startup banner ────────────────────────────────────────────────────────
    var listenUrls = string.Join(", ", builder.WebHost.GetSetting("urls")?.Split(';') ?? ["http://0.0.0.0:8766"]);
    Log.Information("╔══════════════════════════════════════════════════╗");
    Log.Information("║       LlmModelProxy — Anthropic façade           ║");
    Log.Information("╠══════════════════════════════════════════════════╣");
    Log.Information("║  Listening on : {Urls,-38}║", listenUrls);
    Log.Information("║  Usage        : export ANTHROPIC_BASE_URL=<url>  ║");
    Log.Information("║  Claude CLI   : claude  ║");
    Log.Information("╚══════════════════════════════════════════════════╝");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
