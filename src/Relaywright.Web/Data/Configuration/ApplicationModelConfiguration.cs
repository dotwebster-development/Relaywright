using Microsoft.EntityFrameworkCore;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Data.Configuration;

public static class ApplicationModelConfiguration
{
    public static void Configure(ModelBuilder builder, bool isMySql)
    {
        builder.Entity<RelayConfiguration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ListenerBindAddress).HasMaxLength(256);
            entity.Property(x => x.ListenerHostName).HasMaxLength(256);
            entity.Property(x => x.CertificatePath).HasMaxLength(ValidationLimits.MaximumTextLength);
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
            entity.Property(x => x.Owner).HasMaxLength(256);
            entity.Property(x => x.Location).HasMaxLength(256);
            entity.Property(x => x.AllowedSenderAddresses).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.BlockedSenderAddresses).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.AllowedRecipientDomains).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.BlockedRecipientDomains).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.HasIndex(x => x.Cidr).IsUnique();
        });

        builder.Entity<SubmissionPolicy>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.AllowedSenderAddresses).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.BlockedSenderAddresses).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.AllowedRecipientDomains).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
            entity.Property(x => x.BlockedRecipientDomains).HasMaxLength(ValidationLimits.MaximumPolicyListLength);
        });

        builder.Entity<QueuedMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).IsConcurrencyToken();
            entity.Property(x => x.EnvelopeFrom).HasMaxLength(512);
            entity.Property(x => x.RemoteIpAddress).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(64);
            entity.Property(x => x.LastResponseCode).HasMaxLength(32);
            entity.Property(x => x.LastResponseText).HasMaxLength(2048);
            entity.Property(x => x.LastError).HasMaxLength(4096);
            entity.Property(x => x.SpoolFileRelativePath).HasMaxLength(ValidationLimits.MaximumTextLength);
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

        builder.Entity<RuntimeControlState>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.DeliveryPauseReason).HasMaxLength(512);
            entity.Property(x => x.DeliveryPausedBy).HasMaxLength(256);
            entity.Property(x => x.RestartReason).HasMaxLength(512);
            entity.Property(x => x.RestartRequestedBy).HasMaxLength(256);
        });

        builder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.Description).HasMaxLength(ValidationLimits.MaximumTextLength);
            entity.Property(x => x.EmailRecipients).HasMaxLength(ValidationLimits.MaximumTextLength);
            entity.Property(x => x.LastNotificationMessage).HasMaxLength(2048);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasMany(x => x.Results)
                .WithOne(x => x.AlertRule)
                .HasForeignKey(x => x.AlertRuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AlertResult>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Message).HasMaxLength(2048);
            entity.Property(x => x.NotificationMessage).HasMaxLength(2048);
            entity.HasIndex(x => new { x.AlertRuleId, x.OccurredUtc });
        });

        builder.Entity<BackupRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(512);
            entity.Property(x => x.CreatedBy).HasMaxLength(256);
            entity.Property(x => x.Message).HasMaxLength(4096);
            entity.Property(x => x.LastValidationMessage).HasMaxLength(4096);
            entity.HasIndex(x => x.StartedUtc);
        });

        builder.Entity<BackupScheduleState>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
        });

        builder.Entity<DiagnosticRun>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Message).HasMaxLength(2048);
            entity.Property(x => x.RequestedBy).HasMaxLength(256);
            entity.HasIndex(x => new { x.Kind, x.StartedUtc });
            entity.HasMany(x => x.Stages)
                .WithOne(x => x.DiagnosticRun)
                .HasForeignKey(x => x.DiagnosticRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DiagnosticStage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(128);
            entity.Property(x => x.Message).HasMaxLength(2048);
            entity.Property(x => x.Detail).HasMaxLength(4096);
            entity.HasIndex(x => new { x.DiagnosticRunId, x.Sequence });
        });

        builder.Entity<ConfigurationSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Area).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.Summary).HasMaxLength(2048);
            entity.Property(x => x.CreatedBy).HasMaxLength(256);
            entity.HasIndex(x => new { x.Area, x.CreatedUtc });
        });

        ConfigureMySqlLargeTextColumns(builder, isMySql);
    }

    private static void ConfigureMySqlLargeTextColumns(ModelBuilder builder, bool isMySql)
    {
        if (!isMySql)
        {
            return;
        }

        builder.Entity<TrustedNetwork>(entity =>
        {
            entity.Property(x => x.AllowedSenderAddresses).HasColumnType("text");
            entity.Property(x => x.BlockedSenderAddresses).HasColumnType("text");
            entity.Property(x => x.AllowedRecipientDomains).HasColumnType("text");
            entity.Property(x => x.BlockedRecipientDomains).HasColumnType("text");
        });

        builder.Entity<SubmissionPolicy>(entity =>
        {
            entity.Property(x => x.AllowedSenderAddresses).HasColumnType("text");
            entity.Property(x => x.BlockedSenderAddresses).HasColumnType("text");
            entity.Property(x => x.AllowedRecipientDomains).HasColumnType("text");
            entity.Property(x => x.BlockedRecipientDomains).HasColumnType("text");
        });

        builder.Entity<QueuedMessage>(entity =>
        {
            entity.Property(x => x.LastResponseText).HasColumnType("text");
            entity.Property(x => x.LastError).HasColumnType("text");
        });

        builder.Entity<DeliveryAttempt>(entity =>
        {
            entity.Property(x => x.ResponseText).HasColumnType("text");
            entity.Property(x => x.ExceptionMessage).HasColumnType("text");
        });

        builder.Entity<OperationalEvent>(entity =>
        {
            entity.Property(x => x.Message).HasColumnType("text");
            entity.Property(x => x.Detail).HasColumnType("text");
        });

        builder.Entity<AlertRule>(entity =>
        {
            entity.Property(x => x.Description).HasColumnType("text");
            entity.Property(x => x.EmailRecipients).HasColumnType("text");
            entity.Property(x => x.LastNotificationMessage).HasColumnType("text");
        });

        builder.Entity<AlertResult>(entity =>
        {
            entity.Property(x => x.Message).HasColumnType("text");
            entity.Property(x => x.NotificationMessage).HasColumnType("text");
        });

        builder.Entity<BackupRun>(entity =>
        {
            entity.Property(x => x.Message).HasColumnType("text");
            entity.Property(x => x.LastValidationMessage).HasColumnType("text");
        });

        builder.Entity<DiagnosticRun>(entity =>
        {
            entity.Property(x => x.Message).HasColumnType("text");
        });

        builder.Entity<DiagnosticStage>(entity =>
        {
            entity.Property(x => x.Message).HasColumnType("text");
            entity.Property(x => x.Detail).HasColumnType("text");
        });

        builder.Entity<ConfigurationSnapshot>(entity =>
        {
            entity.Property(x => x.Summary).HasColumnType("text");
            entity.Property(x => x.PayloadJson).HasColumnType("text");
        });
    }
}
