using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Identity;

namespace Relaywright.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RelayConfiguration> RelayConfigurations => Set<RelayConfiguration>();

    public DbSet<TrustedNetwork> TrustedNetworks => Set<TrustedNetwork>();

    public DbSet<QueuedMessage> QueuedMessages => Set<QueuedMessage>();

    public DbSet<QueuedMessageRecipient> QueuedMessageRecipients => Set<QueuedMessageRecipient>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<OperationalEvent> OperationalEvents => Set<OperationalEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RelayConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ListenerBindAddress).HasMaxLength(256);
            entity.Property(x => x.ListenerHostName).HasMaxLength(256);
            entity.Property(x => x.CertificatePath).HasMaxLength(1024);
            entity.Property(x => x.UpstreamHost).HasMaxLength(256);
            entity.Property(x => x.UpstreamUserName).HasMaxLength(256);
            entity.Property(x => x.MicrosoftTenantId).HasMaxLength(128);
            entity.Property(x => x.MicrosoftClientId).HasMaxLength(128);
        });

        builder.Entity<TrustedNetwork>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Cidr).HasMaxLength(128);
            entity.Property(x => x.Description).HasMaxLength(256);
            entity.HasIndex(x => x.Cidr).IsUnique();
        });

        builder.Entity<QueuedMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EnvelopeFrom).HasMaxLength(512);
            entity.Property(x => x.RemoteIpAddress).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.Property(x => x.LastResponseCode).HasMaxLength(32);
            entity.Property(x => x.LastResponseText).HasMaxLength(2048);
            entity.Property(x => x.LastError).HasMaxLength(4096);
            entity.Property(x => x.SpoolFileRelativePath).HasMaxLength(1024);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.Status, x.DeliveredUtc });
            entity.HasIndex(x => new { x.Status, x.LastAttemptCompletedUtc });
            entity.HasIndex(x => x.ExpiresUtc);
            entity.HasMany(x => x.Recipients)
                .WithOne(x => x.QueuedMessage)
                .HasForeignKey(x => x.QueuedMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.DeliveryAttempts)
                .WithOne(x => x.QueuedMessage)
                .HasForeignKey(x => x.QueuedMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QueuedMessageRecipient>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RecipientAddress).HasMaxLength(512);
            entity.HasIndex(x => new { x.QueuedMessageId, x.RecipientAddress }).IsUnique();
        });

        builder.Entity<DeliveryAttempt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ResponseCode).HasMaxLength(32);
            entity.Property(x => x.ResponseText).HasMaxLength(2048);
            entity.Property(x => x.ExceptionType).HasMaxLength(256);
            entity.Property(x => x.ExceptionMessage).HasMaxLength(4096);
            entity.HasIndex(x => new { x.QueuedMessageId, x.AttemptNumber }).IsUnique();
        });

        builder.Entity<OperationalEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RemoteIpAddress).HasMaxLength(128);
            entity.Property(x => x.Message).HasMaxLength(2048);
            entity.Property(x => x.Detail).HasMaxLength(8192);
            entity.HasIndex(x => x.OccurredUtc);
            entity.HasIndex(x => new { x.Severity, x.OccurredUtc });
            entity.HasIndex(x => new { x.Category, x.OccurredUtc });
        });
    }
}
