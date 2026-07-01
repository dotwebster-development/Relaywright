namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreSummary
{
    public Guid? BackupId { get; init; }

    public DateTimeOffset? CreatedUtc { get; init; }

    public int ManifestSpoolFileCount { get; init; }

    public int ArchiveSpoolFileCount { get; init; }

    public long QueuedMessageCount { get; init; }

    public bool IncludesCertificateFiles { get; init; }

    public bool IncludesAdminWebListener { get; init; }
}
