using System.Text.Json;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Security;

public sealed class AdminWebListenerConfigurationService(
    AppPaths paths,
    ILogger<AdminWebListenerConfigurationService> logger) : IAdminWebListenerConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static AdminWebListenerConfiguration? LoadConfiguration(AppPaths paths)
    {
        if (!File.Exists(paths.AdminWebListenerConfigurationPath))
        {
            return null;
        }

        var json = File.ReadAllText(paths.AdminWebListenerConfigurationPath);
        var configuration = JsonSerializer.Deserialize<AdminWebListenerConfiguration>(json, JsonOptions)
            ?? throw new InvalidOperationException("Admin web listener configuration could not be read.");
        Validate(configuration);
        return configuration;
    }

    public async Task<AdminWebListenerConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.AdminWebListenerConfigurationPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(paths.AdminWebListenerConfigurationPath);
        var configuration = await JsonSerializer.DeserializeAsync<AdminWebListenerConfiguration>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Admin web listener configuration could not be read.");
        Validate(configuration);
        return configuration;
    }

    public async Task<AdminWebListenerConfiguration> SaveAsync(
        AdminWebListenerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Validate(configuration);

        var saved = new AdminWebListenerConfiguration
        {
            HttpsPort = configuration.HttpsPort,
            EnableHttp = configuration.EnableHttp,
            HttpPort = configuration.HttpPort,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        Directory.CreateDirectory(paths.DataDirectory);
        var tempPath = Path.Combine(paths.DataDirectory, $"{Guid.NewGuid():N}.json");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, saved, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, paths.AdminWebListenerConfigurationPath, overwrite: true);

        logger.LogInformation(
            "Admin web listener configuration saved. HttpsPort={HttpsPort}; HttpEnabled={HttpEnabled}; HttpPort={HttpPort}",
            saved.HttpsPort,
            saved.EnableHttp,
            saved.HttpPort);

        return saved;
    }

    private static void Validate(AdminWebListenerConfiguration configuration)
    {
        ValidatePort(configuration.HttpsPort, "HTTPS port");

        if (configuration.EnableHttp)
        {
            ValidatePort(configuration.HttpPort, "HTTP port");
            if (configuration.HttpPort == configuration.HttpsPort)
            {
                throw new InvalidOperationException("HTTP and HTTPS ports must be different.");
            }
        }
    }

    private static void ValidatePort(int port, string label)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{label} must be between 1 and 65535.");
        }
    }
}
