using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Reports;

internal sealed class ProcessRunner : IProcessRunner
{
    private const int MaxCapturedChars = 8 * 1024 * 1024;

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(spec.FileName)
        {
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Arguments are always passed as a list, never concatenated into a command line.
        foreach (var argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        process.Start();
        process.StandardInput.Close();

        var stdout = ReadCappedAsync(process.StandardOutput);
        var stderr = ReadCappedAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited.
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (builder.Length < MaxCapturedChars)
            {
                builder.Append(buffer, 0, read);
            }
        }

        return builder.ToString();
    }
}
