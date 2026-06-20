using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SmtpServer;
using Relaywright.Web.Configuration;

namespace Relaywright.Web.Services.Smtp;

public sealed class SmtpOptionsFactory(ILogger<SmtpOptionsFactory> logger)
{
    public ISmtpServerOptions Create(RelayConfigurationSnapshot configuration)
    {
        logger.LogInformation(
            "Creating SMTP listener options. Listener={ListenerBindAddress}:{ListenerPort}; HostName={HostName}; MaxMessageSizeBytes={MaxMessageSizeBytes}; StartTls={StartTls}; CertificateConfigured={CertificateConfigured}",
            configuration.ListenerBindAddress,
            configuration.ListenerPort,
            configuration.ListenerHostName,
            configuration.MaxMessageSizeBytes,
            configuration.EnableStartTls,
            !string.IsNullOrWhiteSpace(configuration.CertificatePath));

        var builder = new SmtpServerOptionsBuilder()
            .ServerName(configuration.ListenerHostName)
            .MaxMessageSize((int)Math.Min(int.MaxValue, configuration.MaxMessageSizeBytes))
            .Endpoint(endpoint =>
            {
                endpoint.Endpoint(new IPEndPoint(ParseBindAddress(configuration.ListenerBindAddress), configuration.ListenerPort));

                if (configuration.EnableStartTls && !string.IsNullOrWhiteSpace(configuration.CertificatePath))
                {
                    logger.LogInformation(
                        "Loading SMTP STARTTLS certificate. CertificatePath={CertificatePath}; PasswordConfigured={PasswordConfigured}",
                        configuration.CertificatePath,
                        !string.IsNullOrWhiteSpace(configuration.CertificatePassword));

                    endpoint.Certificate(CreateCertificate(configuration.CertificatePath, configuration.CertificatePassword));
                    endpoint.SupportedSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                }
            });

        return builder.Build();
    }

    private static IPAddress ParseBindAddress(string bindAddress)
    {
        if (string.Equals(bindAddress, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Any;
        }

        if (string.Equals(bindAddress, "::", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.IPv6Any;
        }

        if (IPAddress.TryParse(bindAddress, out var ipAddress))
        {
            return ipAddress;
        }

        throw new InvalidOperationException($"Configured bind address '{bindAddress}' is not a valid IP address.");
    }

    private static X509Certificate2 CreateCertificate(string certificatePath, string? password)
    {
        var extension = Path.GetExtension(certificatePath);
        if (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password,
                X509KeyStorageFlags.EphemeralKeySet,
                Pkcs12LoaderLimits.Defaults);
        }

        return X509CertificateLoader.LoadCertificateFromFile(certificatePath);
    }
}
