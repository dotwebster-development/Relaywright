using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Backups;

public static class BackupRestoreFileSystem
{
    public static void ApplyPendingRestore(AppPaths paths)
    {
        if (!Directory.Exists(paths.RestorePendingDirectory))
        {
            return;
        }

        var markerPath = Path.Combine(paths.RestorePendingDirectory, "restore.json");
        if (!File.Exists(markerPath))
        {
            return;
        }

        var safetyDirectory = Path.Combine(
            paths.DataDirectory,
            $".restore-safety-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(safetyDirectory);

        MoveFileIfExists(paths.DatabasePath, Path.Combine(safetyDirectory, Path.GetFileName(paths.DatabasePath)));
        MoveDirectoryIfExists(paths.SpoolRootDirectory, Path.Combine(safetyDirectory, "spool"));
        MoveDirectoryIfExists(paths.KeyRingDirectory, Path.Combine(safetyDirectory, "keys"));
        MoveDirectoryIfExists(paths.CertificateDirectory, Path.Combine(safetyDirectory, "certs"));
        MoveFileIfExists(paths.AdminHttpsCertificateConfigurationPath, Path.Combine(safetyDirectory, "admin-https-certificate.json"));
        MoveFileIfExists(paths.AdminWebListenerConfigurationPath, Path.Combine(safetyDirectory, "admin-web-listener.json"));

        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "relay.db"), paths.DatabasePath);
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "spool"), paths.SpoolRootDirectory);
        CopyDirectoryIfExists(Path.Combine(paths.RestorePendingDirectory, "certs"), paths.CertificateDirectory);
        CopyFileIfExists(Path.Combine(paths.RestorePendingDirectory, "admin-web-listener.json"), paths.AdminWebListenerConfigurationPath);

        Directory.CreateDirectory(paths.SpoolRootDirectory);
        Directory.CreateDirectory(paths.KeyRingDirectory);
        Directory.CreateDirectory(paths.CertificateDirectory);
        Directory.Delete(paths.RestorePendingDirectory, recursive: true);
    }

    private static void MoveFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
    }

    private static void MoveDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.Move(source, destination);
    }

    private static void CopyFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
