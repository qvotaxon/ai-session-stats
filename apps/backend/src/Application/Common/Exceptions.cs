using System;

namespace SessionInsights.Application.Common;

public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Maps to 409. The optional code lets clients tell recoverable conflicts apart (e.g. session_active).</summary>
public sealed class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

/// <summary>Maps to 422: the request is well-formed but cannot be carried out in the current state.</summary>
public sealed class UnprocessableException(string message) : Exception(message);

public sealed class RequestValidationException(string message) : Exception(message);
