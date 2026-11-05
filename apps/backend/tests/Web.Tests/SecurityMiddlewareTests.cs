using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SessionInsights.Infrastructure.Security;
using Xunit;

namespace SessionInsights.Web.Tests;

public sealed class SecurityFixture : WebApplicationFactory<Program>
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "si-web-tests-" + Guid.NewGuid().ToString("N"));

    public SecurityFixture()
    {
        // The composition root reads configuration eagerly, so point every root at an empty temp folder via the environment.
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("SessionInsights__DataRoot", Path.Combine(root, "data"));
        Environment.SetEnvironmentVariable("SessionInsights__CopilotRoot", Path.Combine(root, "copilot"));
        Environment.SetEnvironmentVariable("SessionInsights__ClaudeRoot", Path.Combine(root, "claude"));
        Environment.SetEnvironmentVariable("SessionInsights__StaticFilesPath", Path.Combine(root, "static"));
        Directory.CreateDirectory(Path.Combine(root, "copilot"));
        Directory.CreateDirectory(Path.Combine(root, "claude"));
        Directory.CreateDirectory(Path.Combine(root, "static"));
        File.WriteAllText(Path.Combine(root, "static", "index.html"), "<html><head></head><body>spa</body></html>");
    }

    public string Token => Services.GetRequiredService<IApiTokenProvider>().Token;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // Best effort: the temp folder is not important.
            }
        }
    }
}

public sealed class SecurityMiddlewareTests(SecurityFixture factory) : IClassFixture<SecurityFixture>
{
    private HttpRequestMessage Get(string url, bool withToken = false, string? host = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (withToken)
        {
            request.Headers.Add("X-Session-Insights-Token", factory.Token);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    private async Task<HttpStatusCode> Status(HttpRequestMessage request)
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task Security_headers_are_sent_on_spa_api_401_health_and_deep_links()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/", "/health", "/api/v1/sessions", "/some/deep/link", "/index.html" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            var csp = response.Headers.GetValues("Content-Security-Policy").Single();
            csp.ShouldContain("default-src 'self'", customMessage: path);
            csp.ShouldContain("frame-ancestors 'none'");
            csp.ShouldContain("object-src 'none'");
            csp.ShouldContain("base-uri 'none'");
            csp.ShouldContain("form-action 'self'");
            csp.ShouldContain("style-src 'self' 'unsafe-inline'");
            csp.ShouldNotContain("script-src");
            response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer", path);
            response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff", path);
            response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY", path);
        }
    }

    [Fact]
    public async Task Security_headers_are_sent_on_a_forbidden_host_response()
    {
        using var client = factory.CreateClient();
        using var request = Get("/health", host: "evil.example");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
        response.Headers.Contains("X-Frame-Options").ShouldBeTrue();
    }

    [Fact]
    public async Task Health_is_anonymous() => (await Status(Get("/health"))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Version_is_anonymous_and_returns_the_application_version()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/version", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("\"version\":\"");
    }

    [Fact]
    public async Task Api_without_token_is_unauthorized() => (await Status(Get("/api/v1/sessions"))).ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Api_with_wrong_token_is_unauthorized()
    {
        var request = Get("/api/v1/sessions");
        request.Headers.Add("X-Session-Insights-Token", "nope");
        (await Status(request)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Api_with_token_returns_an_empty_page() => (await Status(Get("/api/v1/sessions", withToken: true))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Foreign_host_is_forbidden() => (await Status(Get("/health", host: "evil.example"))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Foreign_origin_is_forbidden() =>
        (await Status(Get("/api/v1/sessions", withToken: true, origin: "https://evil.example"))).ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Loopback_origin_is_allowed() =>
        (await Status(Get("/api/v1/sessions", withToken: true, origin: "http://localhost:5173"))).ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task Non_guid_session_id_is_a_bad_request() =>
        (await Status(Get("/api/v1/sessions/not-a-guid", withToken: true))).ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Unknown_session_is_not_found() =>
        (await Status(Get($"/api/v1/sessions/{Guid.NewGuid():D}", withToken: true))).ShouldBe(HttpStatusCode.NotFound);

    [Fact]
    public async Task Index_html_carries_the_token_meta_tag_and_spa_routes_fall_back()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/some/client/route", TestContext.Current.CancellationToken);

        html.ShouldContain("name=\"si-token\"");
        html.ShouldContain(factory.Token);
    }

    [Fact]
    public async Task Unknown_api_routes_do_not_fall_back_to_the_spa() =>
        (await Status(Get("/api/v1/nope", withToken: true))).ShouldBe(HttpStatusCode.NotFound);
}
