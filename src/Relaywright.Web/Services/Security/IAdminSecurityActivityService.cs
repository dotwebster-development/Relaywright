namespace Relaywright.Web.Services.Security;

public interface IAdminSecurityActivityService
{
    Task<AdminLoginActivitySummary> GetLoginActivityAsync(
        string? userName,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SuspiciousLoginSummary> GetSuspiciousLoginSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
