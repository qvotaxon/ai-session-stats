using System;

namespace SessionInsights.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ConflictException(string message) : Exception(message);

public sealed class RequestValidationException(string message) : Exception(message);
