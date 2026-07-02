namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateConfiguration
{
    public AdminHttpsCertificateMode Mode { get; set; }

    public string CertificatePath { get; set; } = string.Empty;

    public string? KeyPath { get; set; }

    public string? ProtectedPassword { get; set; }

    public string[] DnsNames { get; set; } = [];

    public DateTimeOffset? NotAfterUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
