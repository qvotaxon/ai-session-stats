using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SessionInsights.Application.Common;
using SessionInsights.Domain.Sessions;
using SessionInsights.Infrastructure.Security;
using Shouldly;
using Xunit;

namespace SessionInsights.Web.Tests;

[Collection("Security")]
public sealed class StartupTests(SecurityFixture fixture)
{
    [Fact]
    public async Task Serves_the_page_while_initializing_and_defers_report_restoration_until_listening()
    {
        var parsing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new TaskCompletionSource<IReadOnlyList<Session>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var index = Substitute.For<ISessionIndex>();
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            parsing.TrySetResult();
            return new ValueTask<IReadOnlyList<Session>>(sessions.Task.WaitAsync(call.Arg<CancellationToken>()));
        });
        var restoredAfterListening = false;
        using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var reportRegistration = services.Single(d => d.ServiceType == typeof(IReportCoordinator));
            services.AddSingleton<IReportCoordinator>(sp =>
            {
                restoredAfterListening = sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested;
                return (IReportCoordinator)reportRegistration.ImplementationFactory!(sp);
            });
            services.AddSingleton(index);
        }));
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await parsing.Task.WaitAsync(timeout.Token);
            restoredAfterListening.ShouldBeTrue();
            using var startup = await client.GetAsync("/startup", timeout.Token);
            (await startup.Content.ReadAsStringAsync(timeout.Token)).ShouldBe("{\"status\":\"starting\"}");
            startup.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
            using var page = await client.GetAsync("/", timeout.Token);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(timeout.Token)).ShouldContain("spa");
            (await client.GetAsync("/health", timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.GetAsync("/api/v1/sessions", timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            client.DefaultRequestHeaders.Add("X-Session-Insights-Token", app.Services.GetRequiredService<IApiTokenProvider>().Token);
            using var api = await client.GetAsync("/api/v1/sessions", timeout.Token);
            api.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            api.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(1));

            sessions.SetResult([]);
            await app.Services.GetRequiredService<ApplicationReadiness>().WaitAsync(timeout.Token);
            (await client.GetStringAsync("/startup", timeout.Token)).ShouldBe("{\"status\":\"ready\"}");
            (await client.GetAsync("/api/v1/sessions", timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            sessions.TrySetResult([]);
        }
    }

    [Fact]
    public async Task Failed_initialization_is_visible_and_retries_until_ready()
    {
        var attempts = 0;
        var retry = new TaskCompletionSource<IReadOnlyList<Session>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var index = Substitute.For<ISessionIndex>();
        index.GetAllAsync(Arg.Any<CancellationToken>()).Returns(call =>
            Interlocked.Increment(ref attempts) == 1
                ? ValueTask.FromException<IReadOnlyList<Session>>(new InvalidOperationException("Index unavailable"))
                : new ValueTask<IReadOnlyList<Session>>(retry.Task.WaitAsync(call.Arg<CancellationToken>())));
        using var app = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(index);
            services.PostConfigure<SessionInsightsOptions>(options => options.IndexRefreshInterval = TimeSpan.FromMilliseconds(20));
        }));
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (Volatile.Read(ref attempts) < 2)
            {
                await Task.Delay(10, timeout.Token);
            }

            (await client.GetStringAsync("/startup", timeout.Token)).ShouldBe("{\"status\":\"failed\"}");
            retry.SetResult([]);
            await app.Services.GetRequiredService<ApplicationReadiness>().WaitAsync(timeout.Token);
            (await client.GetStringAsync("/startup", timeout.Token)).ShouldBe("{\"status\":\"ready\"}");
        }
        finally
        {
            retry.TrySetResult([]);
        }
    }
}
