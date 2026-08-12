using System.Net;
using MailKit.Security;
using Relaywright.Web.Configuration;
using Relaywright.Web.Data.Entities;
using Relaywright.Web.Validation;

namespace Relaywright.Web.Services.Relay;

public static class RelayConfigurationValidator
{
    public static void Validate(
        RelayConfigurationEditModel model,
        bool hasExistingUpstreamPassword,
        bool hasExistingMicrosoftClientSecret)
    {
        if (string.IsNullOrWhiteSpace(model.ListenerBindAddress))
        {
            throw new InvalidOperationException("Listener bind address is required.");
        }

        if (!IPAddress.TryParse(model.ListenerBindAddress, out _))
        {
            throw new InvalidOperationException("Listener bind address must be a valid IP address.");
        }

        if (model.ListenerPort is < ValidationLimits.MinimumPort or > ValidationLimits.MaximumPort)
        {
            throw new InvalidOperationException(ValidationMessages.PortRange("Listener port"));
        }

        if (string.IsNullOrWhiteSpace(model.ListenerHostName))
        {
            throw new InvalidOperationException("Listener host name is required.");
        }

        ValidateHostOrIp(model.ListenerHostName, "Listener host name");
        if (model.MaxMessageSizeBytes < ValidationLimits.MinimumMessageSizeBytes)
        {
            throw new InvalidOperationException(
                ValidationMessages.AtLeast(
                    "Maximum message size",
                    ValidationLimits.MinimumMessageSizeBytes,
                    "bytes"));
        }

        if (model.EnableStartTls && string.IsNullOrWhiteSpace(model.CertificatePath))
        {
            throw new InvalidOperationException("Certificate path is required when STARTTLS is enabled.");
        }

        if (!string.IsNullOrWhiteSpace(model.CertificatePath))
        {
            ValidateFileExtension(model.CertificatePath, "Certificate path", ValidationLimits.CertificateFileExtensions);
        }

        ValidateOptionalSecret(model.CertificatePassword, "Certificate password");
        if (!string.IsNullOrWhiteSpace(model.UpstreamHost))
        {
            ValidateHostOrIp(model.UpstreamHost, "Upstream host");
        }

        if (model.UpstreamPort is < ValidationLimits.MinimumPort or > ValidationLimits.MaximumPort)
        {
            throw new InvalidOperationException(ValidationMessages.PortRange("Upstream port"));
        }

        ValidatePositiveSettings(model);
        ValidateOptionalSecret(model.UpstreamPassword, "Upstream password");
        ValidateOptionalSecret(model.MicrosoftClientSecret, "Microsoft client secret");
        if (!model.UseUpstreamAuthentication)
        {
            return;
        }

        if (model.UpstreamSecureSocketOptions is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect))
        {
            throw new InvalidOperationException("Upstream authentication requires a guaranteed TLS mode: StartTls or SslOnConnect.");
        }

        if (string.IsNullOrWhiteSpace(model.UpstreamUserName))
        {
            throw new InvalidOperationException("Authentication requires a user name or mailbox.");
        }

        ValidateSingleLineText(model.UpstreamUserName, "Authentication user name");
        if (model.UpstreamAuthenticationMode is null)
        {
            throw new InvalidOperationException("Select an upstream authentication type.");
        }

        switch (model.UpstreamAuthenticationMode.Value)
        {
            case UpstreamAuthenticationMode.Basic:
                if (string.IsNullOrWhiteSpace(model.UpstreamPassword) && !hasExistingUpstreamPassword)
                {
                    throw new InvalidOperationException("Basic authentication requires a password.");
                }
                break;
            case UpstreamAuthenticationMode.Microsoft365OAuth:
                ValidateMicrosoftOAuth(model, hasExistingMicrosoftClientSecret);
                break;
            default:
                throw new InvalidOperationException("The selected upstream authentication mode is not supported.");
        }
    }

    private static void ValidatePositiveSettings(RelayConfigurationEditModel model)
    {
        if (model.DeliveryConcurrency < 1)
            throw new InvalidOperationException("Delivery concurrency must be at least 1.");
        if (model.MaxRetryCount < 1)
            throw new InvalidOperationException("Maximum retry count must be at least 1.");
        if (model.InitialRetryDelaySeconds < 1)
            throw new InvalidOperationException("Initial retry delay must be at least 1 second.");
        if (model.MaxRetryDelaySeconds < model.InitialRetryDelaySeconds)
            throw new InvalidOperationException("Maximum retry delay must be greater than or equal to the initial retry delay.");
        if (model.MessageExpirationHours < 1)
            throw new InvalidOperationException("Message expiration must be at least 1 hour.");
        if (model.DeliveredRetentionHours < 1)
            throw new InvalidOperationException("Delivered retention must be at least 1 hour.");
        if (model.FailedRetentionHours < 1)
            throw new InvalidOperationException("Failed retention must be at least 1 hour.");
        if (model.EventRetentionHours < 1)
            throw new InvalidOperationException("Event retention must be at least 1 hour.");
        if (model.UpstreamTimeoutSeconds < 5)
            throw new InvalidOperationException("Upstream timeout must be at least 5 seconds.");
    }

    private static void ValidateMicrosoftOAuth(
        RelayConfigurationEditModel model,
        bool hasExistingMicrosoftClientSecret)
    {
        if (string.IsNullOrWhiteSpace(model.MicrosoftTenantId))
            throw new InvalidOperationException("Microsoft 365 OAuth requires a tenant ID.");
        if (!ValidationRules.IsMicrosoftTenantId(model.MicrosoftTenantId.Trim()))
            throw new InvalidOperationException("Microsoft 365 OAuth tenant ID must be a tenant GUID or tenant domain.");
        if (string.IsNullOrWhiteSpace(model.MicrosoftClientId))
            throw new InvalidOperationException("Microsoft 365 OAuth requires a client ID.");
        if (!ValidationRules.IsGuidText(model.MicrosoftClientId.Trim()))
            throw new InvalidOperationException("Microsoft 365 OAuth client ID must be a GUID.");
        if (!ValidationRules.IsMailboxAddress(model.UpstreamUserName!.Trim()))
            throw new InvalidOperationException("Microsoft 365 OAuth requires a mailbox-style upstream user name.");
        if (string.IsNullOrWhiteSpace(model.MicrosoftClientSecret) && !hasExistingMicrosoftClientSecret)
            throw new InvalidOperationException("Microsoft 365 OAuth requires a client secret.");
    }

    private static void ValidateHostOrIp(string value, string label)
    {
        if (!ValidationRules.IsHostNameOrIpAddress(value.Trim()))
            throw new InvalidOperationException(ValidationMessages.HostNameOrIpAddress(label));
    }

    private static void ValidateFileExtension(string value, string label, IReadOnlyCollection<string> extensions)
    {
        if (!ValidationRules.HasAllowedExtension(value, extensions))
            throw new InvalidOperationException(ValidationMessages.FileExtension(label, extensions));
    }

    private static void ValidateSingleLineText(string? value, string label)
    {
        if (ValidationRules.ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _))
            throw new InvalidOperationException(ValidationMessages.UnsupportedControlCharacter(label));
    }

    private static void ValidateOptionalSecret(string? value, string label)
    {
        if (value is not null && value.Length > ValidationLimits.MaximumSecretLength)
            throw new InvalidOperationException(ValidationMessages.MaximumLength(label, ValidationLimits.MaximumSecretLength));
        ValidateSingleLineText(value, label);
    }
}
