using System.Buffers;
using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupIncludesDatabaseKeysAndReferencedSpoolFiles()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={appData.Paths.DatabasePath};Pooling=False")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var dbContext = factory.CreateDbContext())
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        var spool = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var messageId = Guid.NewGuid();
        var acceptedUtc = DateTimeOffset.UtcNow;
        var spoolPath = await spool.WriteAsync(
            messageId,
            acceptedUtc,
            new ReadOnlySequence<byte>(TestData.MimeBytes()),
            CancellationToken.None);

        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.QueuedMessages.Add(new QueuedMessage
            {
                Id = messageId,
                SessionId = Guid.NewGuid(),
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = spoolPath,
                Status = QueuedMessageStatus.Pending,
                AcceptedUtc = acceptedUtc,
                CreatedUtc = acceptedUtc,
                NextAttemptAtUtc = acceptedUtc,
                ExpiresUtc = acceptedUtc.AddHours(1),
                Recipients =
                {
                    new QueuedMessageRecipient
                    {
                        RecipientAddress = "recipient@example.test"
                    }
                }
            });
            await dbContext.SaveChangesAsync();
        }

        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var validation = await service.ValidateAsync(run.Id, CancellationToken.None);

        Assert.Equal(BackupRunStatus.Succeeded, run.Status);
        Assert.True(run.LastValidationSucceeded);
        Assert.True(validation.Succeeded);
        var backupPath = await service.GetBackupPathAsync(run.Id, CancellationToken.None);
        Assert.True(File.Exists(backupPath));
        using var archive = ZipFile.OpenRead(backupPath!);
        Assert.NotNull(archive.GetEntry("relay.db"));
        Assert.NotNull(archive.GetEntry("keys/key.xml"));
        Assert.NotNull(archive.GetEntry($"spool/{spoolPath.Replace('\\', '/')}"));
    }

    [Fact]
    public async Task EncryptedBackupRequiresPasswordForValidation()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync(
            "admin",
            scheduled: false,
            CancellationToken.None,
            encryptionPassword: "correct horse battery staple");

        var withoutPassword = await service.ValidateAsync(run.Id, CancellationToken.None);
        var withPassword = await service.ValidateAsync(
            run.Id,
            CancellationToken.None,
            encryptionPassword: "correct horse battery staple");

        Assert.True(run.IsEncrypted);
        Assert.EndsWith(".rwbak", run.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(withoutPassword.Succeeded);
        Assert.True(withPassword.Succeeded);
    }

    [Fact]
    public async Task ReadinessUsesLatestValidatedBackup()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);

        var readiness = await service.GetReadinessAsync(CancellationToken.None);

        Assert.True(run.LastValidationSucceeded);
        Assert.True(readiness.IsReady);
        Assert.Equal(run.Id, readiness.BackupId);
        Assert.True(readiness.BackupStorageBytes > 0);
    }

    [Fact]
    public async Task RestoreServiceStagesAndAppliesBackupOnStartupPath()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var backupService = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<BackupService>.Instance);
        var run = await backupService.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var backupPath = await backupService.GetBackupPathAsync(run.Id, CancellationToken.None);
        Assert.NotNull(backupPath);

        var bytes = await File.ReadAllBytesAsync(backupPath!);
        await using var uploadStream = new MemoryStream(bytes);
        var formFile = new FormFile(uploadStream, 0, bytes.Length, "RestoreInput.BackupFile", Path.GetFileName(backupPath!));
        var restoreService = new BackupRestoreService(
            appData.Paths,
            NullLogger<BackupRestoreService>.Instance);

        var staged = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);
        BackupRestoreService.ApplyPendingRestore(appData.Paths);

        Assert.True(staged.Succeeded);
        Assert.False(Directory.Exists(appData.Paths.RestorePendingDirectory));
        Assert.True(File.Exists(appData.Paths.DatabasePath));
        Assert.True(File.Exists(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml")));
        Assert.NotEmpty(Directory.EnumerateDirectories(appData.Paths.DataDirectory, ".restore-safety-*"));
    }

    [Fact]
    public async Task GetRunsAndPruneRetentionOrderByStartedUtcWithSqliteDateTimeOffsets()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var now = DateTimeOffset.UtcNow;
        var newest = Guid.NewGuid();
        var older = Guid.NewGuid();
        var oldest = Guid.NewGuid();
        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.BackupRuns.AddRange(
                new BackupRun
                {
                    Id = older,
                    StartedUtc = now.AddHours(-2),
                    Status = BackupRunStatus.Succeeded,
                    FileName = "older.zip"
                },
                new BackupRun
                {
                    Id = newest,
                    StartedUtc = now,
                    Status = BackupRunStatus.Succeeded,
                    FileName = "newest.zip"
                },
                new BackupRun
                {
                    Id = oldest,
                    StartedUtc = now.AddHours(-4),
                    Status = BackupRunStatus.Succeeded,
                    FileName = "oldest.zip"
                });
            await dbContext.SaveChangesAsync();
        }

        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            NullLogger<BackupService>.Instance);

        var runs = await service.GetRunsAsync(CancellationToken.None);
        await service.PruneByRetentionAsync(1, CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal([newest, older, oldest], runs.Select(x => x.Id).ToArray());
        Assert.Equal(BackupRunStatus.Succeeded, verifyContext.BackupRuns.Single(x => x.Id == newest).Status);
        Assert.Equal(BackupRunStatus.Deleted, verifyContext.BackupRuns.Single(x => x.Id == older).Status);
        Assert.Equal(BackupRunStatus.Deleted, verifyContext.BackupRuns.Single(x => x.Id == oldest).Status);
    }

    private static async Task<TestDbContextFactory> CreateFileBackedDatabaseAsync(TempAppData appData)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={appData.Paths.DatabasePath};Pooling=False")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var dbContext = factory.CreateDbContext())
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        return factory;
    }
}
