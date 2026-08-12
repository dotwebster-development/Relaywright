using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Relaywright.Web.Pages;
using Relaywright.Web.Services.Alerts;
using Relaywright.Web.Services.Backups;
using Relaywright.Web.Services.ConfigurationHistory;
using Relaywright.Web.Services.Diagnostics;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Services.Security;
using Relaywright.Web.Services.Smtp;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void QueueMaintenanceIsNotPartOfLiveQueueContract()
    {
        Assert.DoesNotContain(
            typeof(IMessageQueueService).GetMethods(),
            method => string.Equals(method.Name, "CleanupAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(IMessageQueueService).GetMethods(),
            method => method.Name is "RetryNowAsync" or "PurgeAsync");
        Assert.Contains(
            typeof(IQueueMaintenanceService).GetMethods(),
            method => string.Equals(method.Name, "CleanupAsync", StringComparison.Ordinal));
        Assert.Contains(
            typeof(IQueueOperatorService).GetMethods(),
            method => string.Equals(method.Name, "RetryNowAsync", StringComparison.Ordinal));
        Assert.False(typeof(IQueueOperatorService).IsAssignableFrom(typeof(MessageQueueService)));
        Assert.True(typeof(IQueueOperatorService).IsAssignableFrom(typeof(QueueOperatorService)));
    }

    [Fact]
    public void DashboardPageUsesACompactApplicationBoundary()
    {
        var constructor = Assert.Single(typeof(IndexModel).GetConstructors());
        Assert.True(
            constructor.GetParameters().Length <= 4,
            $"Dashboard page has {constructor.GetParameters().Length} constructor dependencies.");
    }

    [Fact]
    public void QueueAndLogPagesUseQueryServicesInsteadOfDatabaseContexts()
    {
        var pageTypes = new[]
        {
            typeof(Relaywright.Web.Pages.Queue.IndexModel),
            typeof(Relaywright.Web.Pages.Logs.IndexModel)
        };

        foreach (var pageType in pageTypes)
        {
            var constructor = Assert.Single(pageType.GetConstructors());
            Assert.DoesNotContain(
                constructor.GetParameters(),
                parameter => parameter.ParameterType.IsGenericType
                    && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.IDbContextFactory<>));
        }
    }

    [Fact]
    public void RazorPagesDoNotDirectlyMutateQueueSets()
    {
        var pagesDirectory = Path.Combine(FindRepositoryRoot(), "src", "Relaywright.Web", "Pages");
        var forbiddenFragments = new[]
        {
            "QueuedMessages.Add(",
            "QueuedMessages.AddRange(",
            "QueuedMessages.Remove(",
            "QueuedMessages.RemoveRange(",
            "QueuedMessages.ExecuteDelete",
            "QueuedMessages.ExecuteUpdate"
        };

        var violations = Directory
            .EnumerateFiles(pagesDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => forbiddenFragments
                .Where(fragment => File.ReadAllText(path).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetRelativePath(pagesDirectory, path)}: {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void SmtpServicesDoNotConstructTrustedNetworkImplementations()
    {
        var smtpDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Relaywright.Web",
            "Services",
            "Smtp");
        var forbiddenFragments = new[]
        {
            "new TrustedNetworkService(",
            "new CidrRange("
        };

        var violations = Directory
            .EnumerateFiles(smtpDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => forbiddenFragments
                .Where(fragment => File.ReadAllText(path).Contains(fragment, StringComparison.Ordinal))
                .Select(fragment => $"{Path.GetFileName(path)}: {fragment}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void SubmissionPolicyEvaluatorHasNoPersistenceDependencies()
    {
        var evaluatorPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Relaywright.Web",
            "Services",
            "Security",
            "SubmissionPolicyEvaluator.cs");
        var source = File.ReadAllText(evaluatorPath);
        var forbiddenFragments = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "ApplicationDbContext",
            "IDbContextFactory",
            "DbSet<"
        };

        Assert.DoesNotContain(
            forbiddenFragments,
            fragment => source.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigurationSnapshotServiceUsesDedicatedPayloadAndRestoreOwners()
    {
        var constructor = Assert.Single(typeof(ConfigurationSnapshotService).GetConstructors());
        var dependencyTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(ConfigurationSnapshotPayloadFactory), dependencyTypes);
        Assert.Contains(typeof(ConfigurationSnapshotRestorer), dependencyTypes);

        var servicePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Relaywright.Web",
            "Services",
            "ConfigurationHistory",
            "ConfigurationSnapshotService.cs");
        var source = File.ReadAllText(servicePath);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreRelayAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertServiceUsesDedicatedRuleEvaluationAndStateOwners()
    {
        var constructor = Assert.Single(typeof(AlertService).GetConstructors());
        var dependencyTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(AlertRuleRepository), dependencyTypes);
        Assert.Contains(typeof(AlertEvaluator), dependencyTypes);
        Assert.Contains(typeof(AlertStateCoordinator), dependencyTypes);

        var servicePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Relaywright.Web",
            "Services",
            "Alerts",
            "AlertService.cs");
        var source = File.ReadAllText(servicePath);
        Assert.DoesNotContain("DriveInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AlertResults.Add", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationRules", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupRestoreServiceUsesDedicatedArchiveAndDataValidators()
    {
        var constructor = Assert.Single(typeof(BackupRestoreService).GetConstructors());
        var dependencyTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(BackupRestoreArchiveExtractor), dependencyTypes);
        Assert.Contains(typeof(RestoredBackupValidator), dependencyTypes);

        var servicePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Relaywright.Web",
            "Services",
            "Backups",
            "BackupRestoreService.cs");
        var source = File.ReadAllText(servicePath);
        Assert.DoesNotContain("ZipArchive", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CidrRange", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteConnection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminHttpsCertificateFacadeUsesDedicatedMaterialAndStorageOwners()
    {
        var constructor = Assert.Single(typeof(AdminHttpsCertificateService).GetConstructors());
        var dependencies = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(AdminHttpsCertificateMaterialService), dependencies);
        Assert.Contains(typeof(AdminHttpsCertificateConfigurationStore), dependencies);
        Assert.Contains(typeof(AdminHttpsCertificateFileStore), dependencies);

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Relaywright.Web", "Services", "Security", "AdminHttpsCertificateService.cs"));
        Assert.DoesNotContain("CertificateRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("X509CertificateLoader", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificateMaterialOwnerHasNoDatabaseDependency()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Relaywright.Web", "Services", "Security", "AdminHttpsCertificateMaterialService.cs"));
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDbContextFactory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticTestEmailWorkflowUsesTheSmtpSessionBoundary()
    {
        var constructor = Assert.Single(typeof(UpstreamTestEmailSender).GetConstructors());
        Assert.Contains(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(IUpstreamDiagnosticSmtpSessionFactory));

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Relaywright.Web", "Services", "Diagnostics", "UpstreamTestEmailSender.cs"));
        Assert.DoesNotContain("new SmtpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MailKit.Net.Smtp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramRemainsCompositionInsteadOfFeatureImplementation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Relaywright.Web", "Program.cs"));
        var forbiddenFragments = new[]
        {
            "CertificateRequest",
            "CREATE TABLE",
            "new CidrRange(",
            "QueuedMessages.Add("
        };

        Assert.DoesNotContain(forbiddenFragments, fragment => source.Contains(fragment, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Relaywright.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Relaywright repository root.");
    }
}
