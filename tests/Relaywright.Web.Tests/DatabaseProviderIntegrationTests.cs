using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.Events;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Relay;
using Relaywright.Web.Tests.Support;
using QueueIndexModel = Relaywright.Web.Pages.Queue.IndexModel;
using LogsIndexModel = Relaywright.Web.Pages.Logs.IndexModel;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class DatabaseProviderIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SqlServerProviderCanInitializeSeedAndRunCoreQueuePaths()
    {
        var connectionString = Environment.GetEnvironmentVariable("RELAYWRIGHT_TEST_SQLSERVER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await RunProviderSmokeAsync(TestDatabaseConfiguration.SqlServer(connectionString));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MySqlProviderCanInitializeSeedAndRunCoreQueuePaths()
    {
        var connectionString = Environment.GetEnvironmentVariable("RELAYWRIGHT_TEST_MYSQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await RunProviderSmokeAsync(TestDatabaseConfiguration.MySql(connectionString));
    }

    private static async Task RunProviderSmokeAsync(DatabaseConfiguration databaseConfiguration)
    {
        var options = CreateOptions(databaseConfiguration);
        await using (var resetContext = new ApplicationDbContext(options))
        {
            await resetContext.Database.EnsureDeletedAsync();
        }

        await using var provider = CreateServiceProvider(databaseConfiguration);
        var seeder = provider.GetRequiredService<DataSeeder>();
        await seeder.InitializeAsync(CancellationToken.None);

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var createUser = await userManager.CreateAsync(
            new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@example.test",
                DisplayName = "Administrator",
                EmailConfirmed = true
            },
            "StrongPass123!");
        Assert.True(createUser.Succeeded, string.Join("; ", createUser.Errors.Select(x => x.Description)));

        await using (var verifyContext = new ApplicationDbContext(options))
        {
            Assert.True(await verifyContext.Database.CanConnectAsync());
            Assert.Single(await verifyContext.RelayConfigurations.ToListAsync());
            Assert.NotEmpty(await verifyContext.TrustedNetworks.ToListAsync());
            Assert.NotEmpty(await verifyContext.AlertRules.ToListAsync());
            Assert.Single(await verifyContext.Users.ToListAsync());
        }

        using var appData = TempAppData.Create();
        var factory = new TestDbContextFactory(options);
        var events = new RecordingOperationalEventService();
        var queue = new MessageQueueService(
            factory,
            new RetryDelayCalculator(),
            new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance),
            new ImmediateBackupCoordinator(),
            events,
            new RecordingQueueSignal(),
            databaseConfiguration,
            NullLogger<MessageQueueService>.Instance);

        await ExerciseQueueAsync(queue, factory, appData.Paths, databaseConfiguration);
        await ExercisePagingAsync(factory, databaseConfiguration);
        await ExerciseExternalBackupBehaviorAsync(factory, appData.Paths, databaseConfiguration);
    }

    private static async Task ExerciseQueueAsync(
        MessageQueueService queue,
        TestDbContextFactory factory,
        AppPaths paths,
        DatabaseConfiguration databaseConfiguration)
    {
        var spool = new MessageSpoolService(paths, NullLogger<MessageSpoolService>.Instance);
        var deliveredId = Guid.NewGuid();
        var failedId = Guid.NewGuid();
        var acceptedUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        foreach (var messageId in new[] { deliveredId, failedId })
        {
            var spoolPath = await spool.WriteAsync(
                messageId,
                acceptedUtc,
                new ReadOnlySequence<byte>(TestData.MimeBytes()),
                CancellationToken.None);

            await queue.EnqueueAsync(new NewQueuedMessageRequest
            {
                MessageId = messageId,
                SessionId = Guid.NewGuid(),
                RemoteIpAddress = "127.0.0.1",
                EnvelopeFrom = "sender@example.test",
                Recipients = ["recipient@example.test"],
                SpoolFileRelativePath = spoolPath,
                MessageSizeBytes = 100,
                AcceptedUtc = acceptedUtc,
                MessageExpirationHours = 24
            }, CancellationToken.None);
        }

        var deliveredWork = await queue.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(deliveredWork);
        await queue.MarkDeliveredAsync(deliveredWork!, new DeliveryResult
        {
            Succeeded = true,
            ResponseCode = "250",
            ResponseText = "queued"
        }, CancellationToken.None);

        var retryWork = await queue.TryClaimNextAsync(CancellationToken.None);
        Assert.NotNull(retryWork);
        await queue.MarkFailedAsync(retryWork!, new DeliveryResult
        {
            Succeeded = false,
            FailureCategory = DeliveryFailureCategory.Transient,
            ErrorDetail = "temporary failure"
        }, TestData.Snapshot(), CancellationToken.None);

        await using var dbContext = factory.CreateDbContext();
        Assert.Equal(
            new[] { deliveredId, failedId }.Order(),
            new[] { deliveredWork.MessageId, retryWork.MessageId }.Order());
        Assert.Equal(QueuedMessageStatus.Delivered, (await dbContext.QueuedMessages.SingleAsync(x => x.Id == deliveredWork.MessageId)).Status);
        Assert.Equal(QueuedMessageStatus.RetryScheduled, (await dbContext.QueuedMessages.SingleAsync(x => x.Id == retryWork.MessageId)).Status);
        Assert.True(databaseConfiguration.IsExternalServer);
    }

    private static async Task ExercisePagingAsync(
        TestDbContextFactory factory,
        DatabaseConfiguration databaseConfiguration)
    {
        await using (var dbContext = factory.CreateDbContext())
        {
            dbContext.OperationalEvents.AddRange(
                new OperationalEvent
                {
                    OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                    Message = "older event"
                },
                new OperationalEvent
                {
                    OccurredUtc = DateTimeOffset.UtcNow,
                    Message = "newer event"
                });
            await dbContext.SaveChangesAsync();
        }

        var queueModel = AttachPageContext(new QueueIndexModel(
            factory,
            new NoopQueueService(),
            databaseConfiguration,
            NullLogger<QueueIndexModel>.Instance));
        await queueModel.OnGetAsync("all", CancellationToken.None);
        Assert.True(queueModel.TotalCount >= 2);
        Assert.NotEmpty(queueModel.Messages);

        var logsModel = AttachPageContext(new LogsIndexModel(
            factory,
            databaseConfiguration,
            NullLogger<LogsIndexModel>.Instance));
        await logsModel.OnGetAsync(CancellationToken.None);
        Assert.Contains(logsModel.Events, x => x.Message == "newer event");
    }

    private static async Task ExerciseExternalBackupBehaviorAsync(
        TestDbContextFactory factory,
        AppPaths paths,
        DatabaseConfiguration databaseConfiguration)
    {
        var service = new BackupService(
            factory,
            new BackupCoordinator(),
            new RecordingOperationalEventService(),
            paths,
            databaseConfiguration,
            NullLogger<BackupService>.Instance);

        var run = await service.CreateBackupAsync("admin", scheduled: false, CancellationToken.None);
        var readiness = await service.GetReadinessAsync(CancellationToken.None);

        Assert.Equal(BackupRunStatus.Failed, run.Status);
        Assert.True(readiness.IsReady);
        Assert.Contains("managed outside Relaywright", readiness.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions(DatabaseConfiguration databaseConfiguration)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        databaseConfiguration.Configure(builder);
        return builder.Options;
    }

    private static ServiceProvider CreateServiceProvider(DatabaseConfiguration databaseConfiguration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(databaseConfiguration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
        services.Configure<BootstrapAdminOptions>(_ => { });
        services.AddDbContext<ApplicationDbContext>(options => databaseConfiguration.Configure(options));
        services
            .AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<DataSeeder>();
        return services.BuildServiceProvider();
    }

    private static TModel AttachPageContext<TModel>(TModel model)
        where TModel : PageModel
    {
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return model;
    }

    private sealed class NoopQueueService : IMessageQueueService
    {
        public Task EnqueueAsync(NewQueuedMessageRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<DeliveryWorkItem?> TryClaimNextAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task MarkDeliveredAsync(DeliveryWorkItem workItem, DeliveryResult result, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task MarkFailedAsync(
            DeliveryWorkItem workItem,
            DeliveryResult result,
            RelayConfigurationSnapshot configuration,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<QueueActionResult> RetryNowAsync(Guid messageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<QueueBulkActionResult> RetryNowAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<QueueActionResult> PurgeAsync(Guid messageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<QueueBulkActionResult> PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<int> CleanupAsync(RelayConfigurationSnapshot configuration, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
