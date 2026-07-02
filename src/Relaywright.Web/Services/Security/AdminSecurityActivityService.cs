using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Security;

public sealed class AdminSecurityActivityService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory) : IAdminSecurityActivityService
{
    public const string SignInSucceededMessage = "Admin sign-in succeeded.";
    public const string SignInFailedMessage = "Admin sign-in failed.";

    private static readonly TimeSpan ShortWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    public async Task<AdminLoginActivitySummary> GetLoginActivityAsync(
        string? userName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var observations = await LoadLoginObservationsAsync(cancellationToken);
        var scoped = string.IsNullOrWhiteSpace(userName)
            ? observations
            : observations
                .Where(x => string.Equals(x.UserName, userName, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return AdminLoginActivitySummary.Create(userName, scoped, now, DailyWindow, WeeklyWindow);
    }

    public async Task<SuspiciousLoginSummary> GetSuspiciousLoginSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var observations = await LoadLoginObservationsAsync(cancellationToken);
        return SuspiciousLoginSummary.Create(observations, now, ShortWindow, DailyWindow);
    }

    private async Task<IReadOnlyList<AdminLoginObservation>> LoadLoginObservationsAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var events = await dbContext.OperationalEvents
            .AsNoTracking()
            .Where(x =>
                x.Category == OperationalEventCategory.Security
                && (x.Message == SignInSucceededMessage || x.Message == SignInFailedMessage))
            .ToListAsync(cancellationToken);

        return events
            .Select(TryCreateObservation)
            .OfType<AdminLoginObservation>()
            .OrderByDescending(x => x.OccurredUtc)
            .ToList();
    }

    private static AdminLoginObservation? TryCreateObservation(OperationalEvent entry)
    {
        var detail = ParseDetail(entry.Detail);
        if (!detail.TryGetValue("UserName", out var userName)
            || string.IsNullOrWhiteSpace(userName)
            || !detail.TryGetValue("Result", out var result)
            || string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        var succeeded = string.Equals(entry.Message, SignInSucceededMessage, StringComparison.Ordinal);
        bool? rememberMe = null;
        if (detail.TryGetValue("RememberMe", out var rememberMeValue)
            && bool.TryParse(rememberMeValue, out var parsedRememberMe))
        {
            rememberMe = parsedRememberMe;
        }

        return new AdminLoginObservation(
            entry.OccurredUtc,
            userName,
            succeeded,
            result,
            rememberMe,
            entry.RemoteIpAddress);
    }

    private static Dictionary<string, string> ParseDetail(string? detail)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return values;
        }

        foreach (var part in detail.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == part.Length - 1)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }
}

public sealed record AdminLoginObservation(
    DateTimeOffset OccurredUtc,
    string UserName,
    bool Succeeded,
    string Result,
    bool? RememberMe,
    string? RemoteIpAddress);

public sealed record AdminLoginActivitySummary(
    string? UserName,
    AdminLoginObservation? LastSuccessfulLogin,
    AdminLoginObservation? LastFailedLogin,
    int FailedLast24Hours,
    int FailedLast7Days)
{
    public static AdminLoginActivitySummary Create(
        string? userName,
        IReadOnlyList<AdminLoginObservation> observations,
        DateTimeOffset now,
        TimeSpan dailyWindow,
        TimeSpan weeklyWindow)
    {
        return new AdminLoginActivitySummary(
            userName,
            observations.FirstOrDefault(x => x.Succeeded),
            observations.FirstOrDefault(x => !x.Succeeded),
            observations.Count(x => !x.Succeeded && x.OccurredUtc >= now.Subtract(dailyWindow)),
            observations.Count(x => !x.Succeeded && x.OccurredUtc >= now.Subtract(weeklyWindow)));
    }
}

public sealed record SuspiciousLoginSummary(
    bool IsSuspicious,
    int FailedLast15Minutes,
    int FailedLast24Hours,
    string? MostActiveRemoteIpAddress,
    int MostActiveRemoteIpFailureCount,
    IReadOnlyList<SuspiciousLoginFinding> Findings)
{
    private const int ShortWindowThreshold = 5;
    private const int DailyThreshold = 10;
    private const int SameRemoteIpThreshold = 3;

    public static SuspiciousLoginSummary Empty { get; } = new(false, 0, 0, null, 0, []);

    public static SuspiciousLoginSummary Create(
        IReadOnlyList<AdminLoginObservation> observations,
        DateTimeOffset now,
        TimeSpan shortWindow,
        TimeSpan dailyWindow)
    {
        var failed = observations.Where(x => !x.Succeeded).ToList();
        var shortCutoff = now.Subtract(shortWindow);
        var dailyCutoff = now.Subtract(dailyWindow);
        var failedLast15Minutes = failed.Count(x => x.OccurredUtc >= shortCutoff);
        var failedLast24Hours = failed.Count(x => x.OccurredUtc >= dailyCutoff);
        var topRemoteIp = failed
            .Where(x => x.OccurredUtc >= shortCutoff && !string.IsNullOrWhiteSpace(x.RemoteIpAddress))
            .GroupBy(x => x.RemoteIpAddress!, StringComparer.OrdinalIgnoreCase)
            .Select(x => new { RemoteIpAddress = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.RemoteIpAddress, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var findings = new List<SuspiciousLoginFinding>();
        if (failedLast15Minutes >= ShortWindowThreshold)
        {
            findings.Add(new SuspiciousLoginFinding(
                "Failed logins",
                $"{failedLast15Minutes} failed admin sign-ins in the last 15 minutes.",
                "severity-warning"));
        }

        if (failedLast24Hours >= DailyThreshold)
        {
            findings.Add(new SuspiciousLoginFinding(
                "Daily failed logins",
                $"{failedLast24Hours} failed admin sign-ins in the last 24 hours.",
                "severity-warning"));
        }

        if (topRemoteIp is not null && topRemoteIp.Count >= SameRemoteIpThreshold)
        {
            findings.Add(new SuspiciousLoginFinding(
                "Repeated remote IP",
                $"{topRemoteIp.Count} failed admin sign-ins from {topRemoteIp.RemoteIpAddress} in the last 15 minutes.",
                "severity-warning"));
        }

        return new SuspiciousLoginSummary(
            findings.Count > 0,
            failedLast15Minutes,
            failedLast24Hours,
            topRemoteIp?.RemoteIpAddress,
            topRemoteIp?.Count ?? 0,
            findings);
    }
}

public sealed record SuspiciousLoginFinding(
    string Label,
    string Detail,
    string BadgeClass);
