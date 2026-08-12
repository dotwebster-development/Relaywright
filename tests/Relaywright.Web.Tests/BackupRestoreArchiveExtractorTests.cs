using System.IO.Compression;
using System.Text;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class BackupRestoreArchiveExtractorTests
{
    [Fact]
    public async Task ExtractsAllowedContentAndOmitsKeyMaterial()
    {
        using var appData = TempAppData.Create();
        var archivePath = CreateArchive(
            appData.Root,
            ("spool/message.eml", "message"),
            ("certs/relay.pfx", "certificate"),
            ("keys/key.xml", "secret-key"),
            ("admin-https-certificate.json", "protected-password"));
        var stagingDirectory = Path.Combine(appData.Root, "staging");
        Directory.CreateDirectory(stagingDirectory);

        await new BackupRestoreArchiveExtractor().ExtractAsync(
            archivePath,
            stagingDirectory,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(stagingDirectory, "relay.db")));
        Assert.True(File.Exists(Path.Combine(stagingDirectory, "spool", "message.eml")));
        Assert.True(File.Exists(Path.Combine(stagingDirectory, "certs", "relay.pfx")));
        Assert.False(Directory.Exists(Path.Combine(stagingDirectory, "keys")));
        Assert.False(File.Exists(Path.Combine(stagingDirectory, "admin-https-certificate.json")));
    }

    [Theory]
    [InlineData("spool/../escape.eml")]
    [InlineData("/spool/escape.eml")]
    [InlineData("spool/C:/escape.eml")]
    public async Task RejectsUnsafeArchivePaths(string unsafePath)
    {
        using var appData = TempAppData.Create();
        var archivePath = CreateArchive(appData.Root, (unsafePath, "escape"));
        var stagingDirectory = Path.Combine(appData.Root, "staging");
        Directory.CreateDirectory(stagingDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BackupRestoreArchiveExtractor().ExtractAsync(
                archivePath,
                stagingDirectory,
                CancellationToken.None));

        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateArchive(
        string root,
        params (string Path, string Content)[] additionalEntries)
    {
        var archivePath = Path.Combine(root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", $"{{\"backupId\":\"{Guid.NewGuid()}\",\"application\":\"Relaywright\"}}");
        WriteEntry(archive, "relay.db", "database");
        foreach (var entry in additionalEntries)
        {
            WriteEntry(archive, entry.Path, entry.Content);
        }

        return archivePath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }
}
