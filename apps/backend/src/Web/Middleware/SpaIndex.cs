using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SessionInsights.Infrastructure.Security;

namespace SessionInsights.Web.Middleware;

/// <summary>Serves the SPA's index.html with the API token injected as &lt;meta name="si-token"&gt;. Used for "/", "/index.html" and the SPA fallback.</summary>
public sealed class SpaIndexService(string? staticFilesPath, IApiTokenProvider tokens, ILogger<SpaIndexService> logger)
{
    private readonly Lock gate = new();
    private string? cachedHtml;
    private DateTime cachedStamp;

    public string? StaticFilesPath => staticFilesPath;

    public async Task WriteAsync(HttpContext context)
    {
        var html = Load();
        if (html is null)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, "Not Found", "The frontend has not been built or deployed.");
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html, Encoding.UTF8);
    }

    private string? Load()
    {
        if (string.IsNullOrWhiteSpace(staticFilesPath))
        {
            return null;
        }

        var path = Path.Combine(staticFilesPath, "index.html");
        if (!File.Exists(path))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(path);
        lock (gate)
        {
            if (cachedHtml is not null && stamp == cachedStamp)
            {
                return cachedHtml;
            }

            var raw = File.ReadAllText(path);
            var meta = $"<meta name=\"si-token\" content=\"{WebUtility.HtmlEncode(tokens.Token)}\">";
            var index = raw.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            cachedHtml = index >= 0 ? raw.Insert(index, meta) : meta + raw;
            cachedStamp = stamp;
            logger.LogInformation("Loaded index.html from the configured static files folder");
            return cachedHtml;
        }
    }
}

public sealed class SpaIndexMiddleware(RequestDelegate next, SpaIndexService spa)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (HttpMethods.IsGet(context.Request.Method) && (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)))
        {
            return spa.WriteAsync(context);
        }

        return next(context);
    }
}
