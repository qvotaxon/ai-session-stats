using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Security;

namespace SessionInsights.Web.Middleware;

/// <summary>Rejects (403) any request whose Host or Origin is not a loopback name. Defends against DNS rebinding and cross-site calls.</summary>
public sealed class HostOriginMiddleware(RequestDelegate next, ILogger<HostOriginMiddleware> logger)
{
    private static readonly string[] AllowedHosts = ["localhost", "127.0.0.1", "[::1]"];

    public Task InvokeAsync(HttpContext context)
    {
        if (!IsAllowedHost(context.Request.Host.Host))
        {
            logger.LogWarning("Rejected request with disallowed Host header {HostName}", Sanitise(context.Request.Host.Host));
            return ProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden", "Host is not allowed.");
        }

        if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin.Count > 0)
        {
            if (!Uri.TryCreate(origin[0], UriKind.Absolute, out var uri) || !IsAllowedHost(uri.IsLoopback && uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host.Trim('[', ']')}]" : uri.Host))
            {
                logger.LogWarning("Rejected request with disallowed Origin header");
                return ProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden", "Origin is not allowed.");
            }
        }

        return next(context);
    }

    internal static bool IsAllowedHost(string? host) =>
        host is not null && Array.Exists(AllowedHosts, h => h.Equals(host, StringComparison.OrdinalIgnoreCase));

    private static string Sanitise(string value) => value.Length > 64 ? value[..64] : value;
}

/// <summary>Every /api/** request needs the X-Session-Insights-Token header. /health and static files stay anonymous.</summary>
public sealed class ApiTokenMiddleware(RequestDelegate next, IApiTokenProvider tokens)
{
    public const string HeaderName = "X-Session-Insights-Token";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !tokens.IsValid(context.Request.Headers[HeaderName].ToString()))
        {
            return ProblemWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", "A valid X-Session-Insights-Token header is required.");
        }

        return next(context);
    }
}

/// <summary>Maps application exceptions to RFC 9457 ProblemDetails. Never leaks exception details.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
        catch (RequestValidationException ex)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
        }
        catch (NotFoundException ex)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status404NotFound, "Not Found", ex.Message);
        }
        catch (ConflictException ex)
        {
            await ProblemWriter.WriteAsync(context, StatusCodes.Status409Conflict, "Conflict", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            await ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.");
        }
    }
}

internal static class ProblemWriter
{
    public static Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path },
            options: null,
            contentType: "application/problem+json");
    }
}
