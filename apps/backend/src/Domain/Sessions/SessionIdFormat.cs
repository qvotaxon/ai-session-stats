using System;

namespace SessionInsights.Domain.Sessions;

public static class SessionIdFormat
{
    /// <summary>Session ids are GUIDs in the canonical "D" form. Anything else is rejected (command-injection defence).</summary>
    public static bool IsValid(string? value) =>
        value is { Length: 36 } && Guid.TryParseExact(value, "D", out _);
}

