using System;

namespace SessionInsights.Domain.Sessions;

public sealed record SkillPrompt(string Text, DateTimeOffset? At);
