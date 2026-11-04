using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SessionInsights.Application.Common;

namespace SessionInsights.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(LoggingBehaviour<,>), typeof(ValidationBehaviour<,>)];
        });
        return services;
    }
}
