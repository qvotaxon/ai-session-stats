using System;
using System.IO;
using System.Threading;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Sessions;

internal sealed class SkillAnalysisActivity : ISkillAnalysisActivity
{
    private int active;

    public static string WorkingDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "ai-session-stats-evolve");

    public bool HasActiveAnalysis => Volatile.Read(ref active) > 0;

    public IDisposable BeginAnalysis()
    {
        Interlocked.Increment(ref active);
        return new AnalysisLease(this);
    }

    private sealed class AnalysisLease(SkillAnalysisActivity activity) : IDisposable
    {
        private SkillAnalysisActivity? owner = activity;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.EndAnalysis();
    }

    private void EndAnalysis() => Interlocked.Decrement(ref active);
}
