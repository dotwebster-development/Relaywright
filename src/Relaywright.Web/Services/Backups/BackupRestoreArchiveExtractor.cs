using System.IO.Compression;
using System.Text.Json;

namespace Relaywright.Web.Services.Backups;

public sealed class BackupRestoreArchiveExtractor
{
    private const long MaxExtractedArchiveBytes = 50L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntryCount = 200_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExtractAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Backup manifest is missing.");
        var databaseEntry = archive.GetEntry("relay.db")
            ?? throw new InvalidOperationException("Database snapshot is missing.");

        await using (var manifestStream = manifestEntry.Open())
        {
            var manifest = await JsonSerializer.DeserializeAsync<RestoreManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidOperationException("Backup manifest could not be read.");
            if (!string.Equals(manifest.Application, "Relaywright", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backup manifest is not for Relaywright.");
            }
        }

        ValidateArchiveShape(archive);

        ExtractEntry(databaseEntry, stagingDirectory, "relay.db");
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var restorePath = GetAllowedRestorePath(entry.FullName);
            if (restorePath is not null)
            {
                ExtractEntry(entry, stagingDirectory, restorePath);
            }
        }
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string root, string relativePath)
    {
        if (entry.Length < 0 || entry.Length > MaxExtractedArchiveBytes)
        {
            throw new InvalidOperationException($"Backup entry is too large: {entry.FullName}");
        }

        var destination = ResolveUnder(root, relativePath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        entry.ExtractToFile(destination, overwrite: true);
    }

    private static void ValidateArchiveShape(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxArchiveEntryCount)
        {
            throw new InvalidOperationException("Backup archive contains too many entries.");
        }

        long totalSize = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalizedName = NormalizeArchivePath(entry.FullName);
            if (!names.Add(normalizedName))
            {
                throw new InvalidOperationException(
                    $"Backup archive contains a duplicate entry: {entry.FullName}");
            }

            if (entry.Length < 0)
            {
                throw new InvalidOperationException(
                    $"Backup entry has an invalid length: {entry.FullName}");
            }

            totalSize += entry.Length;
            if (totalSize > MaxExtractedArchiveBytes)
            {
                throw new InvalidOperationException(
                    $"Backup archive expands beyond the restore limit of {MaxExtractedArchiveBytes / 1024 / 1024 / 1024} GB.");
            }

            _ = GetAllowedRestorePath(entry.FullName);
        }
    }

    private static string? GetAllowedRestorePath(string entryName)
    {
        var normalized = NormalizeArchivePath(entryName);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "relay.db", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (normalized.StartsWith("spool/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("certs/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "admin-web-listener.json", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("keys/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "admin-https-certificate.json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        throw new InvalidOperationException($"Backup archive contains an unsupported entry: {entryName}");
    }

    private static string NormalizeArchivePath(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Backup entry uses an absolute path: {entryName}");
        }

        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Contains(':', StringComparison.Ordinal)
                || segment.IndexOfAny(invalidFileNameCharacters) >= 0))
        {
            throw new InvalidOperationException(
                $"Backup entry contains an invalid path segment: {entryName}");
        }

        return string.Join('/', segments);
    }

    private static string ResolveUnder(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("Backup entry resolves outside the restore directory.");
        }

        return candidate;
    }

    private sealed class RestoreManifest
    {
        public Guid BackupId { get; set; }

        public string Application { get; set; } = string.Empty;
    }
}
