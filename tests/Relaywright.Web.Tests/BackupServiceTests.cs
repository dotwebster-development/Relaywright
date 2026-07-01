using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupIncludesDatabaseAndReferencedSpoolFilesButNotKeyMaterial()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");
        await File.WriteAllTextAsync(appData.Paths.AdminHttpsCertificateConfigurationPath, "{\"password\":\"protected\"}");

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
            TestDatabaseConfiguration.Sqlite,
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
        Assert.Null(archive.GetEntry("keys/key.xml"));
        Assert.Null(archive.GetEntry("admin-https-certificate.json"));
        Assert.NotNull(archive.GetEntry($"spool/{spoolPath.Replace('\\', '/')}"));
    }

    [Fact]
    public async Task CreateBackupStripsCredentialStateFromDatabaseSnapshot()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        await SeedCredentialStateAsync(factory);

        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var backupPath = await service.GetBackupPathAsync(run.Id, CancellationToken.None);
        var extractedDatabase = Path.Combine(appData.Root, "extracted-relay.db");
        using (var archive = ZipFile.OpenRead(backupPath!))
        {
            archive.GetEntry("relay.db")!.ExtractToFile(extractedDatabase);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={extractedDatabase};Pooling=False")
            .Options;
        await using var verifyContext = new ApplicationDbContext(options);
        var restoredConfiguration = await verifyContext.RelayConfigurations.SingleAsync();

        Assert.Empty(await verifyContext.Users.ToListAsync());
        Assert.Empty(await verifyContext.ConfigurationSnapshots.ToListAsync());
        Assert.Empty(await verifyContext.OperationalEvents.ToListAsync());
        Assert.Empty(await verifyContext.DiagnosticRuns.ToListAsync());
        Assert.Empty(await verifyContext.BackupRuns.ToListAsync());
        Assert.Empty(await verifyContext.AlertResults.ToListAsync());
        Assert.False(restoredConfiguration.UseUpstreamAuthentication);
        Assert.Null(restoredConfiguration.ProtectedCertificatePassword);
        Assert.Null(restoredConfiguration.ProtectedUpstreamPassword);
        Assert.Null(restoredConfiguration.ProtectedMicrosoftClientSecret);
        Assert.Null((await verifyContext.AlertRules.SingleAsync()).LastNotificationMessage);
    }

    [Fact]
    public async Task EncryptedBackupRequiresPasswordForValidation()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
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
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);

        var readiness = await service.GetReadinessAsync(CancellationToken.None);

        Assert.True(run.LastValidationSucceeded);
        Assert.True(readiness.IsReady);
        Assert.Equal(run.Id, readiness.BackupId);
        Assert.True(readiness.BackupStorageBytes > 0);
    }

    [Fact]
    public async Task ExternalDatabaseModeReportsBackupsAsExternallyManaged()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var configuration = TestDatabaseConfiguration.SqlServer(
            "Server=localhost;Database=Relaywright;User Id=relaywright;Password=secret;TrustServerCertificate=True");
        var events = new RecordingOperationalEventService();
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            events,
            appData.Paths,
            configuration,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var readiness = await service.GetReadinessAsync(CancellationToken.None);
        await using var uploadStream = new MemoryStream([1, 2, 3]);
        var formFile = new FormFile(uploadStream, 0, uploadStream.Length, "RestoreBackupFile", "backup.zip");
        var restoreService = new BackupRestoreService(
            appData.Paths,
            configuration,
            NullLogger<BackupRestoreService>.Instance);
        var restore = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);

        Assert.Equal(BackupRunStatus.Failed, run.Status);
        Assert.Contains("only available for SQLite", run.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(readiness.IsReady);
        Assert.Contains("managed outside Relaywright", readiness.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(restore.Succeeded);
        Assert.Contains("managed outside Relaywright", restore.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events.Events, x => x.Severity == EventSeverity.Warning);
    }

    [Fact]
    public async Task RestoreServiceStagesAndAppliesBackupOnStartupPath()
    {
        using var appData = TempAppData.Create();
        await File.WriteAllTextAsync(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml"), "<key />");
        await File.WriteAllTextAsync(appData.Paths.AdminHttpsCertificateConfigurationPath, "{\"password\":\"protected\"}");
        var factory = await CreateFileBackedDatabaseAsync(appData);
        var backupService = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupService>.Instance);
        var run = await backupService.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var backupPath = await backupService.GetBackupPathAsync(run.Id, CancellationToken.None);
        Assert.NotNull(backupPath);

        var bytes = await File.ReadAllBytesAsync(backupPath!);
        await using var uploadStream = new MemoryStream(bytes);
        var formFile = new FormFile(uploadStream, 0, bytes.Length, "RestoreBackupFile", Path.GetFileName(backupPath!));
        var restoreService = new BackupRestoreService(
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupRestoreService>.Instance);

        var staged = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);
        BackupRestoreService.ApplyPendingRestore(appData.Paths);

        Assert.True(staged.Succeeded);
        Assert.NotNull(staged.Summary);
        Assert.Equal(run.Id, staged.Summary.BackupId);
        Assert.NotNull(staged.Summary.CreatedUtc);
        Assert.False(Directory.Exists(appData.Paths.RestorePendingDirectory));
        Assert.True(File.Exists(appData.Paths.DatabasePath));
        Assert.False(File.Exists(Path.Combine(appData.Paths.KeyRingDirectory, "key.xml")));
        Assert.False(File.Exists(appData.Paths.AdminHttpsCertificateConfigurationPath));
        Assert.NotEmpty(Directory.EnumerateDirectories(appData.Paths.DataDirectory, ".restore-safety-*"));
    }

    [Fact]
    public async Task RestoreServiceStripsCredentialStateFromLegacyBackup()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        await SeedCredentialStateAsync(factory);
        var queuedMessageId = Guid.NewGuid();
        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.QueuedMessages.Add(new QueuedMessage
            {
                Id = queuedMessageId,
                SessionId = Guid.NewGuid(),
                EnvelopeFrom = "sender@example.test",
                SpoolFileRelativePath = "legacy.eml",
                Status = QueuedMessageStatus.Failed,
                AttemptCount = 1,
                LastResponseText = "contains token-value",
                LastError = "contains super-secret-value",
                AcceptedUtc = DateTimeOffset.UtcNow,
                CreatedUtc = DateTimeOffset.UtcNow,
                NextAttemptAtUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
                DeliveryAttempts =
                {
                    new DeliveryAttempt
                    {
                        AttemptNumber = 1,
                        StartedUtc = DateTimeOffset.UtcNow,
                        CompletedUtc = DateTimeOffset.UtcNow,
                        Succeeded = false,
                        ResponseText = "contains token-value",
                        ExceptionType = nameof(InvalidOperationException),
                        ExceptionMessage = "contains super-secret-value"
                    }
                }
            });
            await dbContext.SaveChangesAsync();
        }

        var backupBytes = await CreatePlainBackupBytesAsync(appData.Paths.DatabasePath);
        await using var uploadStream = new MemoryStream(backupBytes);
        var formFile = new FormFile(uploadStream, 0, backupBytes.Length, "RestoreBackupFile", "legacy.zip");
        var restoreService = new BackupRestoreService(
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupRestoreService>.Instance);

        var staged = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);
        BackupRestoreService.ApplyPendingRestore(appData.Paths);

        Assert.True(staged.Succeeded);
        await using var verifyContext = factory.CreateDbContext();
        var restoredConfiguration = await verifyContext.RelayConfigurations.SingleAsync();
        Assert.Empty(await verifyContext.Users.ToListAsync());
        Assert.Empty(await verifyContext.ConfigurationSnapshots.ToListAsync());
        Assert.Empty(await verifyContext.OperationalEvents.ToListAsync());
        Assert.Empty(await verifyContext.DiagnosticRuns.ToListAsync());
        Assert.Empty(await verifyContext.BackupRuns.ToListAsync());
        Assert.Empty(await verifyContext.AlertResults.ToListAsync());
        Assert.False(restoredConfiguration.UseUpstreamAuthentication);
        Assert.Null(restoredConfiguration.ProtectedCertificatePassword);
        Assert.Null(restoredConfiguration.ProtectedUpstreamPassword);
        Assert.Null(restoredConfiguration.ProtectedMicrosoftClientSecret);
        Assert.Null((await verifyContext.AlertRules.SingleAsync()).LastNotificationMessage);
        var queuedMessage = await verifyContext.QueuedMessages
            .Include(x => x.DeliveryAttempts)
            .SingleAsync(x => x.Id == queuedMessageId);
        Assert.Equal(QueuedMessageStatus.Failed, queuedMessage.Status);
        Assert.Null(queuedMessage.LastResponseText);
        Assert.Null(queuedMessage.LastError);
        var deliveryAttempt = Assert.Single(queuedMessage.DeliveryAttempts);
        Assert.Null(deliveryAttempt.ResponseText);
        Assert.Null(deliveryAttempt.ExceptionMessage);
    }

    [Fact]
    public async Task RestoreServiceRejectsUnsupportedArchiveEntries()
    {
        using var appData = TempAppData.Create();
        _ = await CreateFileBackedDatabaseAsync(appData);
        var backupBytes = await CreatePlainBackupBytesAsync(
            appData.Paths.DatabasePath,
            archive =>
            {
                var entry = archive.CreateEntry("spool/../relay.db");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("nope");
            });
        await using var uploadStream = new MemoryStream(backupBytes);
        var formFile = new FormFile(uploadStream, 0, backupBytes.Length, "RestoreBackupFile", "malicious.zip");
        var restoreService = new BackupRestoreService(
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupRestoreService>.Instance);

        var staged = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);

        Assert.False(staged.Succeeded);
        Assert.Contains("invalid path segment", staged.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreServiceRejectsOverlappingTrustedNetworksFromBackup()
    {
        using var appData = TempAppData.Create();
        var factory = await CreateFileBackedDatabaseAsync(appData);
        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.TrustedNetworks.AddRange(
                new TrustedNetwork
                {
                    Cidr = "10.0.0.0/8",
                    Description = "broad",
                    IsEnabled = true
                },
                new TrustedNetwork
                {
                    Cidr = "10.10.0.0/16",
                    Description = "specific",
                    IsEnabled = true
                });
            await dbContext.SaveChangesAsync();
        }

        var backupBytes = await CreatePlainBackupBytesAsync(appData.Paths.DatabasePath);
        await using var uploadStream = new MemoryStream(backupBytes);
        var formFile = new FormFile(uploadStream, 0, backupBytes.Length, "RestoreBackupFile", "overlap.zip");
        var restoreService = new BackupRestoreService(
            appData.Paths,
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupRestoreService>.Instance);

        var staged = await restoreService.StageRestoreAsync(formFile, null, CancellationToken.None);

        Assert.False(staged.Succeeded);
        Assert.Contains("overlapping trusted networks", staged.Message, StringComparison.OrdinalIgnoreCase);
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
            TestDatabaseConfiguration.Sqlite,
            NullLogger<BackupService>.Instance);

        var runs = await service.GetRunsAsync(CancellationToken.None);
        await service.PruneByRetentionAsync(1, CancellationToken.None);

        await using var verifyContext = factory.CreateDbContext();
        Assert.Equal([newest, older, oldest], runs.Select(x => x.Id).ToArray());
        Assert.Equal(BackupRunStatus.Succeeded, verifyContext.BackupRuns.Single(x => x.Id == newest).Status);
        Assert.Equal(BackupRunStatus.Deleted, verifyContext.BackupRuns.Single(x => x.Id == older).Status);
        Assert.Equal(BackupRunStatus.Deleted, verifyContext.BackupRuns.Single(x => x.Id == oldest).Status);
    }

    private static async Task SeedCredentialStateAsync(TestDbContextFactory factory)
    {
        await using var dbContext = factory.CreateDbContext();
        dbContext.RelayConfigurations.Add(new RelayConfiguration
        {
            Id = 1,
            ListenerBindAddress = "127.0.0.1",
            ListenerPort = 2525,
            ListenerHostName = "relaywright.test",
            UpstreamHost = "smtp.example.test",
            UseUpstreamAuthentication = true,
            UpstreamUserName = "relay@example.test",
            ProtectedCertificatePassword = "protected-certificate-password",
            ProtectedUpstreamPassword = "protected-upstream-password",
            ProtectedMicrosoftClientSecret = "protected-microsoft-secret"
        });
        dbContext.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = "admin",
            NormalizedUserName = "ADMIN",
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            EmailConfirmed = true,
            PasswordHash = "hashed-password"
        });
        dbContext.ConfigurationSnapshots.Add(new ConfigurationSnapshot
        {
            Id = Guid.NewGuid(),
            Area = ConfigurationSnapshotService.RelayArea,
            DisplayName = "Relay Settings",
            Summary = "contains protected secret blobs",
            PayloadJson = "{\"protectedUpstreamPassword\":\"protected-upstream-password\"}",
            CreatedUtc = DateTimeOffset.UtcNow
        });
        dbContext.OperationalEvents.Add(new OperationalEvent
        {
            Severity = EventSeverity.Error,
            Category = OperationalEventCategory.Delivery,
            Message = "old error",
            Detail = "contains super-secret-value"
        });
        dbContext.DiagnosticRuns.Add(new DiagnosticRun
        {
            Id = Guid.NewGuid(),
            Kind = DiagnosticRunKind.Connectivity,
            Message = "contains super-secret-value",
            Stages =
            {
                new DiagnosticStage
                {
                    Name = "Token",
                    Status = DiagnosticStageStatus.Failed,
                    Message = "contains super-secret-value",
                    Detail = "contains token-value"
                }
            }
        });
        dbContext.BackupRuns.Add(new BackupRun
        {
            Id = Guid.NewGuid(),
            StartedUtc = DateTimeOffset.UtcNow,
            Status = BackupRunStatus.Failed,
            Message = "contains super-secret-value"
        });
        dbContext.AlertRules.Add(new AlertRule
        {
            Key = "test-alert",
            DisplayName = "Test Alert",
            Description = "Test alert",
            Threshold = 1,
            LastNotificationMessage = "contains super-secret-value",
            Results =
            {
                new AlertResult
                {
                    Message = "contains super-secret-value",
                    NotificationMessage = "contains token-value",
                    Threshold = 1,
                    ObservedValue = 2
                }
            }
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<byte[]> CreatePlainBackupBytesAsync(
        string databasePath,
        Action<ZipArchive>? configure = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntryFromFile(databasePath, "relay.db", CompressionLevel.Optimal);
            configure?.Invoke(archive);
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(
                manifestStream,
                new
                {
                    backupId = Guid.NewGuid(),
                    application = "Relaywright"
                });
        }

        return stream.ToArray();
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
