using Microsoft.Extensions.Options;
using Relaywright.Web.Options;

namespace Relaywright.Web.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string contentRootPath, IOptions<StorageOptions> options)
        : this(contentRootPath, options.Value)
    {
    }

    public AppPaths(string contentRootPath, StorageOptions options)
    {
        ContentRootPath = contentRootPath;
        DataDirectory = Resolve(contentRootPath, options.DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, options.DatabaseFileName);
        SpoolRootDirectory = Path.Combine(DataDirectory, options.SpoolDirectoryName);
        KeyRingDirectory = Path.Combine(DataDirectory, options.KeyDirectoryName);
        CertificateDirectory = Path.Combine(DataDirectory, options.CertificateDirectoryName);
        AdminHttpsCertificateConfigurationPath = Path.Combine(DataDirectory, options.AdminHttpsCertificateFileName);
        AdminWebListenerConfigurationPath = Path.Combine(DataDirectory, options.AdminWebListenerFileName);
    }

    public string ContentRootPath { get; }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string SpoolRootDirectory { get; }

    public string KeyRingDirectory { get; }

    public string CertificateDirectory { get; }

    public string AdminHttpsCertificateConfigurationPath { get; }

    public string AdminWebListenerConfigurationPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(SpoolRootDirectory);
        Directory.CreateDirectory(KeyRingDirectory);
        Directory.CreateDirectory(CertificateDirectory);
    }

    public string CreateSpoolRelativePath(Guid messageId, DateTimeOffset timestampUtc)
    {
        var folder = Path.Combine(
            timestampUtc.UtcDateTime.ToString("yyyy"),
            timestampUtc.UtcDateTime.ToString("MM"),
            timestampUtc.UtcDateTime.ToString("dd"));

        return Path.Combine(folder, $"{messageId:N}.eml");
    }

    public string GetSpoolAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Spool path is required.");
        }

        var normalizedRelativePath = NormalizeSpoolRelativePath(relativePath);
        var root = Path.GetFullPath(SpoolRootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException("Spool path resolves outside the spool directory.");
        }

        return candidate;
    }

    private static string NormalizeSpoolRelativePath(string relativePath)
    {
        var normalized = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Spool path must be relative.");
        }

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.None);
        if (segments.Any(segment => segment is "." or ".." || string.IsNullOrWhiteSpace(segment)))
        {
            throw new InvalidOperationException("Spool path contains invalid path segments.");
        }

        return normalized;
    }

    private static string Resolve(string contentRootPath, string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }
}
