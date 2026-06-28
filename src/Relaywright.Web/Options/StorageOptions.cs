namespace Relaywright.Web.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DataDirectory { get; set; } = "App_Data";

    public string DatabaseFileName { get; set; } = "relay.db";

    public string SpoolDirectoryName { get; set; } = "spool";

    public string KeyDirectoryName { get; set; } = "keys";

    public string BackupDirectoryName { get; set; } = "backups";

    public string RestorePendingDirectoryName { get; set; } = "restore-pending";

    public string CertificateDirectoryName { get; set; } = "certs";

    public string AdminHttpsCertificateFileName { get; set; } = "admin-https-certificate.json";

    public string AdminWebListenerFileName { get; set; } = "admin-web-listener.json";
}
