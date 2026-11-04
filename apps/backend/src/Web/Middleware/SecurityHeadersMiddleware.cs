using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace SessionInsights.Web.Middleware;

/// <summary>Adds browser hardening headers to every response, including static files, the SPA index, errors and short-circuits.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; style-src 'self' 'unsafe-inline'";

    // Scalar loads its UI from a CDN and uses inline scripts: only relaxed for the dev-only documentation pages.
    private const string DocsContentSecurityPolicy =
        "default-src 'self' https:; img-src 'self' data: https:; script-src 'self' 'unsafe-inline' https:; style-src 'self' 'unsafe-inline' https:; font-src 'self' data: https:; connect-src 'self' https:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var docsPage = environment.IsDevelopment()
            && (context.Request.Path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase));

        context.Response.OnStarting(static state =>
        {
            var (response, csp) = ((HttpResponse, string))state;
            var headers = response.Headers;
            headers["Content-Security-Policy"] = csp;
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            return Task.CompletedTask;
        }, (context.Response, docsPage ? DocsContentSecurityPolicy : ContentSecurityPolicy));

        return next(context);
    }
}
