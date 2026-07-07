using System.ComponentModel.DataAnnotations;
using MailKit.Security;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Configuration;

public sealed class RelayConfigurationEditModel : IValidatableObject
{
    [RequiredNonWhiteSpace]
    [StringLength(256)]
    [IpAddress]
    public string ListenerBindAddress { get; set; } = "0.0.0.0";

    [PortNumber]
    public int ListenerPort { get; set; } = 25;

    [RequiredNonWhiteSpace]
    [StringLength(256)]
    [HostNameOrIpAddress]
    public string ListenerHostName { get; set; } = Environment.MachineName;

    [Range(typeof(long), "1024", "9223372036854775807")]
    public long MaxMessageSizeBytes { get; set; } = 10 * 1024 * 1024;

    public bool EnableStartTls { get; set; }

    [StringLength(ValidationLimits.MaximumTextLength)]
    [NoControlCharacters]
    [AllowedFileExtensions(".pfx", ".p12", ".cer", ".crt", ".pem")]
    public string? CertificatePath { get; set; }

    [StringLength(ValidationLimits.MaximumSecretLength)]
    [NoControlCharacters]
    public string? CertificatePassword { get; set; }

    [StringLength(256)]
    [HostNameOrIpAddress]
    public string UpstreamHost { get; set; } = string.Empty;

    [PortNumber]
    public int UpstreamPort { get; set; } = 587;

    public SecureSocketOptions UpstreamSecureSocketOptions { get; set; } = SecureSocketOptions.StartTlsWhenAvailable;

    public bool UseUpstreamAuthentication { get; set; }

    public UpstreamAuthenticationMode? UpstreamAuthenticationMode { get; set; }

    [StringLength(256)]
    [NoControlCharacters]
    public string? UpstreamUserName { get; set; }

    [StringLength(ValidationLimits.MaximumSecretLength)]
    [NoControlCharacters]
    public string? UpstreamPassword { get; set; }

    [StringLength(128)]
    [MicrosoftTenantId]
    public string? MicrosoftTenantId { get; set; }

    [StringLength(128)]
    [GuidText]
    public string? MicrosoftClientId { get; set; }

    [StringLength(ValidationLimits.MaximumSecretLength)]
    [NoControlCharacters]
    public string? MicrosoftClientSecret { get; set; }

    [Range(5, int.MaxValue)]
    public int UpstreamTimeoutSeconds { get; set; } = 30;

    [Range(1, int.MaxValue)]
    public int DeliveryConcurrency { get; set; } = 1;

    [Range(1, int.MaxValue)]
    public int MaxRetryCount { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int InitialRetryDelaySeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int MaxRetryDelaySeconds { get; set; } = 3600;

    [Range(1, int.MaxValue)]
    public int MessageExpirationHours { get; set; } = 72;

    [Range(1, int.MaxValue)]
    public int DeliveredRetentionHours { get; set; } = 24;

    [Range(1, int.MaxValue)]
    public int FailedRetentionHours { get; set; } = 168;

    [Range(1, int.MaxValue)]
    public int EventRetentionHours { get; set; } = 720;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EnableStartTls && string.IsNullOrWhiteSpace(CertificatePath))
        {
            yield return new ValidationResult(
                "Certificate path is required when STARTTLS is enabled.",
                [nameof(CertificatePath)]);
        }

        if (MaxRetryDelaySeconds < InitialRetryDelaySeconds)
        {
            yield return new ValidationResult(
                "Maximum retry delay must be greater than or equal to the initial retry delay.",
                [nameof(MaxRetryDelaySeconds)]);
        }

        if (!UseUpstreamAuthentication)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(UpstreamUserName))
        {
            yield return new ValidationResult(
                "Authentication requires a user name or mailbox.",
                [nameof(UpstreamUserName)]);
        }

        if (UpstreamAuthenticationMode is null)
        {
            yield return new ValidationResult(
                "Select an upstream authentication type.",
                [nameof(UpstreamAuthenticationMode)]);
            yield break;
        }

        if (UpstreamAuthenticationMode == Relaywright.Web.Data.Entities.UpstreamAuthenticationMode.Microsoft365OAuth
            && !string.IsNullOrWhiteSpace(UpstreamUserName)
            && !ValidationRules.IsMailboxAddress(UpstreamUserName.Trim()))
        {
            yield return new ValidationResult(
                "Microsoft 365 OAuth requires a mailbox-style upstream user name.",
                [nameof(UpstreamUserName)]);
        }
    }
}
