namespace Relaywright.Web.Services.Security;

public interface IAdminWebListenerConfigurationService
{
    Task<AdminWebListenerConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken);

    Task<AdminWebListenerConfiguration> SaveAsync(
        AdminWebListenerConfiguration configuration,
        CancellationToken cancellationToken);
}
