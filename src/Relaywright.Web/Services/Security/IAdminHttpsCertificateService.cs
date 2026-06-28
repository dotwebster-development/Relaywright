namespace Relaywright.Web.Services.Security;

public interface IAdminHttpsCertificateService
{
    Task<AdminHttpsCertificateConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken);

    Task<AdminHttpsCertificateConfiguration> SavePfxAsync(
        IFormFile certificateFile,
        string? password,
        CancellationToken cancellationToken);

    Task<AdminHttpsCertificateConfiguration> SavePemAsync(
        IFormFile certificateFile,
        IFormFile keyFile,
        string? keyPassword,
        CancellationToken cancellationToken);

    Task<AdminHttpsCertificateConfiguration> GenerateSelfSignedAsync(
        string dnsNames,
        int validYears,
        CancellationToken cancellationToken);
}
