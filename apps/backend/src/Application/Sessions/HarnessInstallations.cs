using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SessionInsights.Application.Sessions;

public sealed record HarnessInstallationDto(string Version, bool? Enabled, string? Scope);

public sealed record HarnessInventoryDto(
    string Source,
    string Status,
    DateTimeOffset CheckedAt,
    IReadOnlyList<HarnessInstallationDto> Installations,
    string? Error);

public interface IHarnessInventory
{
    Task<IReadOnlyList<HarnessInventoryDto>> GetAsync(CancellationToken cancellationToken, bool refresh = false);
}

public sealed record HarnessUpdateDto(
    string Source,
    string Status,
    string? LatestVersion,
    bool Available,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    string? CompletionStatus = null,
    string? CompletionError = null);

public interface IHarnessUpdates
{
    IReadOnlyList<HarnessUpdateDto> Get();
    HarnessUpdateDto Start(string source);
    void Check();
}
