namespace Relaywright.Web.Services.Security;

public sealed record GeneratedAdminHttpsCertificate(
    byte[] PfxBytes,
    string Password,
    string[] DnsNames,
    DateTimeOffset NotAfterUtc);
