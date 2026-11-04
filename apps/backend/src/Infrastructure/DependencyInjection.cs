using System;
using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SessionInsights.Application.Common;
using SessionInsights.Infrastructure.Reports;
using SessionInsights.Infrastructure.Security;
using SessionInsights.Infrastructure.Sessions;

namespace SessionInsights.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFileSystem, FileSystem>();

        services.AddSingleton<ISessionSourceReader, CopilotSessionReader>();
        services.AddSingleton<ISessionSourceReader, ClaudeSessionReader>();
        services.AddSingleton<IExcludedSessions, ExcludedSessionsStore>();
        services.AddSingleton<ISessionIndex, SessionIndex>();
        services.AddHostedService<SessionIndexWarmer>();

        services.AddSingleton<ReportActivityTracker>();
        services.AddSingleton<IReportActivity>(sp => sp.GetRequiredService<ReportActivityTracker>());
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IExecutableLocator, ExecutableLocator>();
        services.AddSingleton<ReportCoordinator>();
        services.AddSingleton<IReportCoordinator>(sp => sp.GetRequiredService<ReportCoordinator>());
        services.AddHostedService<ReportWorker>();

        services.AddSingleton<IApiTokenProvider, ApiTokenProvider>();
        return services;
    }
}
