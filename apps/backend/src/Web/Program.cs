using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using SessionInsights.Application;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure;
using SessionInsights.Infrastructure.Security;
using SessionInsights.ServiceDefaults;
using SessionInsights.Web.Middleware;

const string ServiceName = "SessionInsights";

var builder = WebApplication.CreateBuilder(args);

// Composition root: the only place that reads IConfiguration. Everything else receives IOptions<SessionInsightsOptions>.
var bootstrap = new SessionInsightsOptions();
builder.Configuration.GetSection(SessionInsightsOptions.SectionName).Bind(bootstrap);
builder.Services.Configure<SessionInsightsOptions>(builder.Configuration.GetSection(SessionInsightsOptions.SectionName));
builder.Services.PostConfigure<SessionInsightsOptions>(o =>
    o.StaticFilesPath ??= builder.Configuration["Frontend:StaticFilesPath"]);

builder.Host.UseWindowsService(options => options.ServiceName = ServiceName);
builder.Host.UseSerilog((context, services, logger) => logger
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(bootstrap.ResolveDataRoot(), "logs", "session-insights-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));

// Loopback only: Kestrel never listens on another interface, whatever ASPNETCORE_URLS says.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, bootstrap.Port));

builder.AddServiceDefaults();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var staticFilesPath = ResolveStaticFilesPath(bootstrap.StaticFilesPath ?? builder.Configuration["Frontend:StaticFilesPath"], builder.Environment);
builder.Services.AddSingleton(sp => new SpaIndexService(staticFilesPath, sp.GetRequiredService<IApiTokenProvider>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SpaIndexService>>()));

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<HostOriginMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiTokenMiddleware>();
app.UseMiddleware<SpaIndexMiddleware>();

if (staticFilesPath is not null && Directory.Exists(staticFilesPath))
{
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(staticFilesPath) });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapGet("/version", () => new
{
    version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
});
app.MapControllers();

// SPA fallback (never for /api): unknown GET routes get index.html so client-side routing works.
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || !HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    return context.RequestServices.GetRequiredService<SpaIndexService>().WriteAsync(context);
});

app.Run();

static string? ResolveStaticFilesPath(string? configured, IHostEnvironment environment)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured, environment.ContentRootPath);
    }

    var published = Path.Combine(environment.ContentRootPath, "wwwroot");
    if (File.Exists(Path.Combine(published, "index.html")) || !environment.IsDevelopment())
    {
        return published;
    }

    // Development: apps/backend/src/Web -> apps/frontend/dist
    return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "..", "frontend", "dist"));
}

public partial class Program;
