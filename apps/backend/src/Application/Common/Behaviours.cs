using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.Logging;

namespace SessionInsights.Application.Common;

public sealed class LoggingBehaviour<TMessage, TResponse>(ILogger<LoggingBehaviour<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TMessage).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await next(message, cancellationToken);
        }
        finally
        {
            logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

public sealed class ValidationBehaviour<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (message is IValidatable validatable && validatable.Validate() is { } error)
        {
            throw new RequestValidationException(error);
        }

        return next(message, cancellationToken);
    }
}
