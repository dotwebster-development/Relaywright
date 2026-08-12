using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;
using Relaywright.Web.Data.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;

namespace Relaywright.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RelayConfiguration> RelayConfigurations => Set<RelayConfiguration>();
    public DbSet<TrustedNetwork> TrustedNetworks => Set<TrustedNetwork>();
    public DbSet<SubmissionPolicy> SubmissionPolicies => Set<SubmissionPolicy>();
    public DbSet<QueuedMessage> QueuedMessages => Set<QueuedMessage>();
    public DbSet<QueuedMessageRecipient> QueuedMessageRecipients => Set<QueuedMessageRecipient>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();
    public DbSet<RuntimeControlState> RuntimeControlStates => Set<RuntimeControlState>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<AlertResult> AlertResults => Set<AlertResult>();
    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();
    public DbSet<BackupScheduleState> BackupScheduleStates => Set<BackupScheduleState>();
    public DbSet<DiagnosticRun> DiagnosticRuns => Set<DiagnosticRun>();
    public DbSet<DiagnosticStage> DiagnosticStages => Set<DiagnosticStage>();
    public DbSet<ConfigurationSnapshot> ConfigurationSnapshots => Set<ConfigurationSnapshot>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ApplicationModelConfiguration.Configure(builder, Database.IsMySql());
    }
}
