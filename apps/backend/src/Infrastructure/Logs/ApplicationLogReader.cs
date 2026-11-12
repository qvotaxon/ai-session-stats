using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;
using SessionInsights.Application.Logs;

namespace SessionInsights.Infrastructure.Logs;

internal sealed class ApplicationLogReader(IFileSystem fs, IOptions<SessionInsightsOptions> options) : IApplicationLog
{
    private const int MaxBytes = 256 * 1024;

    public async Task<ApplicationLogDto> ReadAsync(CancellationToken cancellationToken)
    {
        var directory = fs.Path.Combine(options.Value.ResolveDataRoot(), "logs");
        var file = fs.Directory.Exists(directory)
            ? fs.Directory.EnumerateFiles(directory, "session-insights-*.log")
                .OrderByDescending(path => fs.Path.GetFileName(path), StringComparer.Ordinal)
                .FirstOrDefault()
            : null;
        if (file is null)
        {
            return new ApplicationLogDto(null, string.Empty, false);
        }

        using var stream = fs.FileStream.New(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var truncated = length > MaxBytes;
        var buffer = new byte[(int)Math.Min(length, MaxBytes)];
        stream.Seek(Math.Max(0, length - MaxBytes), SeekOrigin.Begin);
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        var content = Encoding.UTF8.GetString(buffer, 0, read);
        if (truncated)
        {
            // The tail can start inside a UTF-8 character or log line; omit that partial line.
            var newline = content.IndexOf('\n');
            content = newline < 0 ? string.Empty : content[(newline + 1)..];
        }

        return new ApplicationLogDto(fs.Path.GetFileName(file), content, truncated);
    }
}
