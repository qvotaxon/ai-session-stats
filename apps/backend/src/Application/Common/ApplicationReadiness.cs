using System.Threading;
using System.Threading.Tasks;

namespace SessionInsights.Application.Common;

public sealed class ApplicationReadiness
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string status = "starting";

    public string Status => Volatile.Read(ref status);

    public Task WaitAsync(CancellationToken cancellationToken) => ready.Task.WaitAsync(cancellationToken);

    public void MarkReady()
    {
        Volatile.Write(ref status, "ready");
        ready.TrySetResult();
    }

    public void MarkFailed() => Volatile.Write(ref status, "failed");
}
