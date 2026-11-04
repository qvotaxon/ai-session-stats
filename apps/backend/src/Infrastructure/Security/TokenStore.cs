using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Options;
using SessionInsights.Application.Common;

namespace SessionInsights.Infrastructure.Security;

public interface IApiTokenProvider
{
    string Token { get; }

    bool IsValid(string? candidate);
}

/// <summary>
/// Generates a random token on first run and stores it in DataRoot\token with a user-only ACL.
/// Uses the real file system on purpose: the ACL cannot be faked.
/// </summary>
internal sealed class ApiTokenProvider : IApiTokenProvider
{
    private readonly byte[] tokenBytes;

    public ApiTokenProvider(IOptions<SessionInsightsOptions> options)
    {
        var path = Path.Combine(options.Value.ResolveDataRoot(), "token");
        Token = LoadOrCreate(path);
        tokenBytes = Encoding.UTF8.GetBytes(Token);
    }

    public string Token { get; }

    public bool IsValid(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(candidate);
        return bytes.Length == tokenBytes.Length && CryptographicOperations.FixedTimeEquals(bytes, tokenBytes);
    }

    private static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32)
            {
                return existing;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        if (OperatingSystem.IsWindows())
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using var stream = FileSystemAclExtensions.Create(new FileInfo(path), FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security);
            stream.Write(Encoding.UTF8.GetBytes(token));
        }
        else
        {
            File.WriteAllText(path, token);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return token;
    }
}
